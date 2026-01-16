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
    private const int DefaultTimeoutMs = 90_000;

    public PromptCache(int maxSize = 1000)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Gets a cached result or creates a new one using the provided function.
    /// Handles request deduplication - if another thread is already fetching the same key,
    /// this thread will wait for that result instead of making a duplicate request.
    /// </summary>
    public string GetOrCreate(string prompt, string instructionId, Func<string> createValue, int timeoutMs = 0)
    {
        var cacheKey = BuildCacheKey(prompt, instructionId);
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
                CleanupPendingRequestLocked(cacheKey);
            }

            return null;
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

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _accessOrder.Clear();
            _cacheNodes.Clear();

            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    private static string BuildCacheKey(string prompt, string instructionId)
    {
        var normalizedPrompt = NormalizePrompt(prompt);
        return string.IsNullOrEmpty(instructionId)
            ? normalizedPrompt
            : $"{normalizedPrompt}||{instructionId.ToLowerInvariant()}";
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
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.PromptCache: error waiting for pending request: {ex.Message}");
            return null;
        }
    }
}
