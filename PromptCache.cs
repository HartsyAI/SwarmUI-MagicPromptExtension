using System.Security.Cryptography;
using System.Text;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension;

public class PromptCache
{
    private readonly Dictionary<string, string> _cache = new();
    private readonly LinkedList<string> _accessOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _cacheNodes = new();
    private readonly Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new();
    private readonly object _lock = new();

    private readonly int _maxSize;
    private const int DefaultTimeoutMs = 20_000;

    public PromptCache(int maxSize = 1000)
    {
        _maxSize = maxSize;
    }

    /// <summary>Gets a cached result or creates one with <paramref name="createValue"/>. The key covers the prompt, the model
    /// and the resolved instruction text, so a changed model, variable or instruction never returns a stale response.
    /// If another thread is already fetching the same key, this thread waits for that result instead of making a duplicate request.</summary>
    public string GetOrCreate(string prompt, string modelId, string instructions, Func<string> createValue, int timeoutMs = 0)
    {
        string cacheKey = BuildCacheKey(prompt, modelId, instructions);
        var effectiveTimeout = timeoutMs > 0 ? timeoutMs : DefaultTimeoutMs;
        TaskCompletionSource<string> pendingTcs = null;

        lock (_lock)
        {
            if (TryGetFromCacheLocked(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            if (_pendingRequests.TryGetValue(cacheKey, out var existingTcs))
            {
                Logs.Debug("MagicPromptExtension.PromptCache: another thread is already fetching this prompt, waiting...");
                pendingTcs = existingTcs;
            }
            else
            {
                // We are the owner - create a TaskCompletionSource for other threads to wait on
                var tcs = new TaskCompletionSource<string>();
                _pendingRequests[cacheKey] = tcs;
            }
        }

        // If another thread was already fetching, wait for it OUTSIDE the lock
        if (pendingTcs != null)
        {
            return WaitForPendingRequest(pendingTcs, effectiveTimeout);
        }

        // Make the request OUTSIDE the lock to avoid blocking other threads
        string result;
        try
        {
            result = createValue();
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.PromptCache: factory failed: {ex.Message}");

            lock (_lock)
            {
                // Signal waiting threads with the error so they don't hang until timeout
                if (_pendingRequests.TryGetValue(cacheKey, out var tcs))
                {
                    tcs.TrySetException(ex);
                }
                CleanupPendingRequestLocked(cacheKey);
            }

            throw; // Re-throw so GetLlmResponse can convert to SwarmReadableErrorException
        }

        lock (_lock)
        {
            if (result != null)
            {
                AddToCacheLocked(cacheKey, result);
            }

            SignalPendingRequestLocked(cacheKey, result);
            CleanupPendingRequestLocked(cacheKey);
        }

        return result;
    }

    private static string BuildCacheKey(string prompt, string modelId, string instructions)
    {
        string normalizedPrompt = NormalizePrompt(prompt);
        string instructionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(instructions ?? string.Empty)));
        return $"{normalizedPrompt}||{(modelId ?? string.Empty).ToLowerInvariant()}||{instructionHash}";
    }

    private static string NormalizePrompt(string prompt)
    {
        return string.IsNullOrWhiteSpace(prompt)
            ? string.Empty
            : new string(prompt.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    private bool TryGetFromCacheLocked(string key, out string cachedResult)
    {
        if (!_cache.TryGetValue(key, out cachedResult))
        {
            return false;
        }

        Logs.Debug("MagicPromptExtension.PromptCache: cache hit");
        if (_cacheNodes.TryGetValue(key, out var node))
        {
            _accessOrder.Remove(node);
            _accessOrder.AddLast(node);
        }
        return true;
    }

    private void AddToCacheLocked(string key, string value)
    {
        while (_cache.Count >= _maxSize)
        {
            var oldestNode = _accessOrder.First;
            if (oldestNode != null)
            {
                var oldestKey = oldestNode.Value;
                _cache.Remove(oldestKey);
                _cacheNodes.Remove(oldestKey);
                _accessOrder.RemoveFirst();
            }
            else
            {
                break;
            }
        }

        _cache[key] = value;

        if (_cacheNodes.TryGetValue(key, out var existingNode))
        {
            _accessOrder.Remove(existingNode);
        }

        var newNode = _accessOrder.AddLast(key);
        _cacheNodes[key] = newNode;
    }

    private void SignalPendingRequestLocked(string key, string result)
    {
        if (_pendingRequests.TryGetValue(key, out var tcs))
        {
            tcs.TrySetResult(result ?? string.Empty);
        }
    }

    private void CleanupPendingRequestLocked(string key)
    {
        _pendingRequests.Remove(key);
    }

    private static string WaitForPendingRequest(TaskCompletionSource<string> tcs, int timeoutMs)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!tcs.Task.Wait(timeoutMs))
            {
                Logs.Warning($"MagicPromptExtension.PromptCache: timeout after {timeoutMs}ms waiting for pending request");
                return null;
            }

            stopwatch.Stop();
            var result = tcs.Task.Result;

            if (string.IsNullOrEmpty(result))
            {
                Logs.Debug("MagicPromptExtension.PromptCache: waited for owner request, but got empty result");
                return null;
            }

            Logs.Debug($"MagicPromptExtension.PromptCache: waited {stopwatch.ElapsedMilliseconds}ms for owner request");
            return result;
        }
        catch (OperationCanceledException)
        {
            Logs.Debug("MagicPromptExtension.PromptCache: pending request was cancelled");
            return null;
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            Logs.Debug("MagicPromptExtension.PromptCache: pending request was cancelled");
            return null;
        }
        catch (AggregateException ex) when (ex.InnerException is not OperationCanceledException)
        {
            Logs.Error($"MagicPromptExtension.PromptCache: pending request failed: {ex.InnerException.Message}");
            throw ex.InnerException; // Re-throw the factory error so it propagates to the caller
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.PromptCache: error waiting for pending request: {ex.Message}");
            return null;
        }
    }
}
