/**
 * magicprompt.js
 * Core functionality and utilities for the MagicPrompt extension.
 */

'use strict';

// Initialize MagicPrompt global namespace if it doesn't exist
if (!window.MagicPrompt) {
    window.MagicPrompt = {
        initialized: false,
        settings: {
            backend: 'ollama',
            baseurl: 'http://localhost:11434',
            model: '',
            visionbackend: 'ollama',
            visionbaseurl: 'http://localhost:11434',
            visionmodel: '',
            unloadmodel: false,
            backends: {
                openai: { apikey: '' },
                anthropic: { apikey: '' },
                openrouter: { apikey: '' }
            },
            instructions: {
                chat: '',
                vision: '',
                caption: ''
            }
        },
        APIClient: {
            /**
             * Makes an API request using SwarmUI's genericRequest
             * @param {Object} payload - Request payload
             * @returns {Promise<Object>} API response
             */
            async makeRequest(payload) {
                if (!payload) {
                    throw new Error('Invalid payload');
                }
                // Allow handling of existing requests for vision mode
                const isVisionRequest = payload.messageType === 'Vision';
                const currentMode = document.getElementById('vision_mode')?.checked;
                if (!isVisionRequest && currentMode) {
                    const currentImage = window.visionHandler?.getCurrentImage();
                    if (currentImage) {
                        payload.messageContent.media = [{
                            type: "base64",
                            data: currentImage
                        }];
                        payload.messageType = 'Vision';
                    }
                }
                try {
                    return new Promise((resolve, reject) => {
                        genericRequest('PhoneHomeAsync', payload,
                            data => {
                                if (data.success) {
                                    console.log('API request successful:', data);
                                    resolve(data);
                                } else {
                                    console.error('API request failed:', data.error);
                                    reject(new Error(data.error || 'API request failed'));
                                }
                            }
                        );
                    });
                } catch (error) {
                    console.error('API request error:', error);
                    throw error;
                }
            }
        },

        RequestBuilder: {
            /**
             * Creates a request payload for API calls
             * @param {string} input - User input text
             * @param {string|null} image - Base64 image data
             * @param {string} action - Action type
             * @returns {Object} Formatted request payload
             */
            createRequestPayload(input, image, action) {
                if (!input?.trim()) {
                    throw new Error('Input is required');
                }
                const isVisionRequest = Boolean(image);
                const visionMode = document.getElementById('vision_mode')?.checked;
                if (action !== "magic" && visionMode && !image) {
                    return {
                        text: "Please upload an image first to use vision mode.",
                        error: true
                    };
                }
                try {
                    const modelId = this.getModelId(isVisionRequest);
                    const backend = isVisionRequest ? MP.settings.visionbackend : MP.settings.backend;
                    const instructions = this.getInstructions(action, isVisionRequest);
                    const text = instructions ? `${instructions}\n\nUser: ${input}` : input;
                    return {
                        messageContent: {
                            text,
                            systemPrompt: this.getSystemPrompt(action, visionMode),
                            media: image ? [{
                                type: "base64",
                                data: image,
                                mediaType: window.visionHandler?.currentMediaType || "image/jpeg"
                            }] : null
                        },
                        modelId,
                        messageType: isVisionRequest ? "Vision" : "Text",
                        action,
                        keep_alive: backend === 'ollama' && MP.settings.unloadmodel ? 0 : null
                    };
                } catch (error) {
                    console.error('Error creating request payload:', error);
                    throw error;
                }
            },
            getModelId(isVision) {
                const modelId = isVision
                    ? document.getElementById('visionModel')?.value
                    : document.getElementById('modelSelect')?.value;

                if (!modelId) {
                    throw new Error('Please select a model first');
                }
                return modelId;
            },
            getSystemPrompt(action, visionMode) {
                if (action === "magic") {
                    return "You are an AI assistant specialized in improving image generation prompts. Help users create more detailed and effective prompts.";
                }
                return visionMode
                    ? "You are an AI assistant with advanced vision capabilities. When the user provides an image, analyze it in detail and respond to their questions about the image."
                    : "You are a helpful AI assistant. Engage in natural conversation and provide assistance with any questions or tasks the user has.";
            },
            getInstructions(action, isVision) {
                const instructions = MP.settings.instructions || {};
                if (action === 'magic') {
                    return null; // No instructions for magic action
                }
                if (isVision && instructions.vision) {
                    return instructions.vision;
                }
                if (!isVision && instructions.chat) {
                    return instructions.chat;
                }
                return null;
            }
        },

        ResponseHandler: {
            handleResponse(response, action) {
                if (!response) {
                    console.error('No response received');
                    return this.showError('No response received from LLM');
                }
                if (response.error) {
                    console.error('Response error:', response.error);
                    return this.showError(response.error);
                }
                if (!response.response) {
                    console.error('Response missing content');
                    return this.showError('Empty response received from LLM');
                }
                try {
                    if (action === "magic") {
                        this.handleMagicResponse(response.response);
                    }
                    return response.response;
                } catch (error) {
                    console.error('Error handling response:', error);
                    this.showError(error.message);
                    return null;
                }
            },
            handleMagicResponse(response) {
                const promptBox = document.getElementById('alt_prompt_textbox');
                if (promptBox) {
                    promptBox.value = response;
                    triggerChangeFor(promptBox);
                    promptBox.focus();
                    promptBox.setSelectionRange(0, promptBox.value.length);
                }
            },
            showError(error) {
                let errorMessage = error;
                if (typeof error === 'string' && error.includes('Provider returned error')) {
                    const match = error.match(/Provider returned error.*?:(.*)/);
                    if (match?.[1]) {
                        errorMessage = match[1].trim();
                    }
                }
                showError(errorMessage);
                return null;
            }
        }
    }
};

// Use existing MP or create new reference
window.MP = window.MP || window.MagicPrompt;

/**
 * Loads settings from the backend
 * @returns {Promise<Object>} Settings object
 * @throws {Error} If settings cannot be loaded
 */
async function loadSettings() {
    try {
        const response = await new Promise((resolve, reject) => {
            genericRequest('GetSettingsAsync', {}, response => {
                if (response.success) {
                    console.log('Raw settings from backend:', response.settings);

                    // Get raw settings directly from response
                    const serverSettings = response.settings;

                    // Create settings object preserving server values
                    const settings = {
                        backend: serverSettings.backend || 'ollama',
                        model: serverSettings.model || '',
                        visionbackend: serverSettings.visionbackend || serverSettings.backend || 'ollama',
                        visionmodel: serverSettings.visionmodel || '',
                        unloadmodel: serverSettings.unloadmodel || false,
                        baseurl: serverSettings.baseurl || 'http://localhost:11434',
                        visionbaseurl: serverSettings.visionbaseurl || serverSettings.baseurl || 'http://localhost:11434',
                        backends: serverSettings.backends || {
                            openai: { apikey: '' },
                            anthropic: { apikey: '' },
                            openrouter: { apikey: '' }
                        },
                        instructions: {
                            chat: serverSettings.instructions?.chat || '',
                            vision: serverSettings.instructions?.vision || '',
                            caption: serverSettings.instructions?.caption || ''
                        }
                    };

                    console.log('Structured settings:', settings);
                    MP.settings = settings;
                    resolve(settings);
                } else {
                    const error = new Error(response.error || 'Failed to load settings');
                    console.error('Settings load error:', error);
                    reject(error);
                }
            });
        });

        return response;
    } catch (error) {
        console.error('Settings load error:', error);
        throw error;
    }
}

/**
 * Saves current settings to the backend
 * @returns {Promise<void>}
 */
async function saveSettings() {
    try {
        const backendMap = {
            'ollamaLLMBtn': 'ollama',
            'openrouterLLMBtn': 'openrouter',
            'openaiLLMBtn': 'openai',
            'openaiAPILLMBtn': 'openaiapi',
            'anthropicLLMBtn': 'anthropic'
        };
        // Get selected chat and vision backends
        const selectedChatBackend = document.querySelector('input[name="llmBackend"]:checked');
        const selectedVisionBackend = document.querySelector('input[name="visionBackendSelect"]:checked');
        // Get selected models
        const chatModel = document.getElementById('modelSelect')?.value;
        const visionModel = document.getElementById('visionModel')?.value;
        // Debug current selections
        console.log('Current selections:', {
            chatBackend: selectedChatBackend?.id,
            visionBackend: selectedVisionBackend?.id,
            chatModel,
            visionModel
        });
        // Match exact structure expected by C# DefaultSettings
        const settings = {
            backend: selectedChatBackend ? backendMap[selectedChatBackend.id] : MP.settings.backend,
            model: chatModel || MP.settings.model,
            visionbackend: selectedVisionBackend ? backendMap[selectedVisionBackend.id.replace('VisionBtn', 'LLMBtn')] : MP.settings.visionbackend,
            visionmodel: visionModel || MP.settings.visionmodel,
            unloadmodel: document.getElementById('unload_models_toggle')?.checked,
            baseurl: document.getElementById('backendUrl')?.value || MP.settings.baseurl,
            visionbaseurl: document.getElementById('visionBackendUrl')?.value || MP.settings.visionbaseurl,
            instructions: {
                chat: document.getElementById('chatInstructions')?.value || MP.settings.instructions?.chat || '',
                vision: document.getElementById('visionInstructions')?.value || MP.settings.instructions?.vision || '',
                caption: document.getElementById('captionInstructions')?.value || MP.settings.instructions?.caption || ''
            },
            backends: {
                ...MP.settings.backends // Preserve existing backend configurations
            }
        };
        console.log('Saving settings with structure:', settings);
        const response = await new Promise((resolve, reject) => {
            genericRequest('SaveSettingsAsync', { settings }, data => {
                if (data.success) {
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Failed to save settings'));
                }
            });
        });
        // Update local settings
        MP.settings = {
            ...response.settings
        };
        console.log('Settings saved successfully:', MP.settings);
        // Refresh models
        await fetchModels();
    } catch (error) {
        console.error('Settings save error:', error);
        showError(`Failed to save settings: ${error.message}`);
        throw error;
    }
}

/**
 * Resets settings to defaults
 * @returns {Promise<void>}
 */
async function resetSettings() {
    try {
        if (!confirm('This will reset all settings to defaults. Any custom API keys will be cleared. Are you sure?')) {
            return;
        }
        const response = await new Promise((resolve, reject) => {
            genericRequest('ResetSettingsAsync', {}, response => {
                if (response.success) {
                    resolve(response);
                } else {
                    reject(new Error(response.error || 'Failed to reset settings'));
                }
            });
        });
        MP.settings = response.settings;
        await fetchModels();
        // Reset UI elements
        document.getElementById('chatInstructions').value = '';
        document.getElementById('visionInstructions').value = '';
        document.getElementById('captionInstructions').value = '';
        closeSettingsModal();
    } catch (error) {
        console.error('Settings reset error:', error);
        showError(`Failed to reset settings: ${error.message}`);
    }
}

/**
 * Fetches available models from the backend
 * @returns {Promise<void>}
 */
async function fetchModels() {
    const modelSelect = document.getElementById("modelSelect");
    const visionModelSelect = document.getElementById("visionModel");
    
    if (!modelSelect || !visionModelSelect) {
        console.error('Model select elements not found');
        return;
    }

    console.log('Starting fetchModels with settings:', {
        backend: MP.settings.backend,
        visionbackend: MP.settings.visionbackend,
        model: MP.settings.model,
        visionmodel: MP.settings.visionmodel
    });

    try {
        // Clear existing options
        modelSelect.innerHTML = '';
        visionModelSelect.innerHTML = '';

        // Add default options
        const defaultOption = new Option('-- Select a model --', '');
        modelSelect.add(defaultOption.cloneNode(true));
        visionModelSelect.add(defaultOption.cloneNode(true));

        // Fetch models for both backends
        console.log('Fetching models for backends:', MP.settings.backend, MP.settings.visionbackend);
        const response = await new Promise((resolve, reject) => {
            genericRequest('GetModelsAsync', {}, data => {
                if (data.success) {
                    console.log('Received models response:', {
                        chatModels: data.models,
                        visionModels: data.visionmodels
                    });
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Failed to fetch models'));
                }
            });
        });

        // Add chat models
        if (Array.isArray(response.models)) {
            console.log('Adding chat models to modelSelect');
            response.models.forEach(model => {
                if (!model.model) {
                    console.warn('Chat model missing model field:', model);
                    return;
                }
                const option = new Option(model.name || model.model, model.model);
                modelSelect.add(option);
            });
            if (MP.settings.model) {
                console.log('Setting saved chat model:', MP.settings.model);
                setModelIfExists(modelSelect, MP.settings.model);
            }
        }

        // Add vision models
        if (Array.isArray(response.visionmodels)) {
            console.log('Adding vision models to visionModelSelect');
            response.visionmodels.forEach(model => {
                if (!model.model) {
                    console.warn('Vision model missing model field:', model);
                    return;
                }
                const option = new Option(model.name || model.model, model.model);
                visionModelSelect.add(option);
            });
            if (MP.settings.visionmodel) {
                console.log('Setting saved vision model:', MP.settings.visionmodel);
                setModelIfExists(visionModelSelect, MP.settings.visionmodel);
            }
        }

        console.log('Final model select contents:', {
            chat: Array.from(modelSelect.options).map(o => o.value),
            vision: Array.from(visionModelSelect.options).map(o => o.value)
        });

    } catch (error) {
        console.error('Error fetching models:', error);
        showError(`Failed to fetch models: ${error.message}`);
    }
}

/**
 * Updates model select elements with fetched models
 * @private
 * @param {HTMLSelectElement} modelSelect - LLM model select element
 * @param {HTMLSelectElement} visionModelSelect - Vision model select element
 * @param {Array} models - Array of model data
 */
function updateModelSelects(modelSelect, visionModelSelect, models) {
    // Clear existing options
    modelSelect.innerHTML = '';
    visionModelSelect.innerHTML = '';
    // Add default option
    const defaultOption = new Option('-- Select a model --', '');
    modelSelect.add(defaultOption.cloneNode(true));
    visionModelSelect.add(defaultOption);
    // Add models to select elements
    if (Array.isArray(models)) {
        models.forEach(model => {
            if (!model.model) {
                console.warn('Model missing model field:', model);
                return;
            }
            const option = new Option(model.name || model.model, model.model);
            modelSelect.add(option.cloneNode(true));
            visionModelSelect.add(option);
        });
        // Restore selected models
        if (MP.settings.model) {
            setModelIfExists(modelSelect, MP.settings.model);
        }
        if (MP.settings.visionmodel) {
            setModelIfExists(visionModelSelect, MP.settings.visionmodel);
        }
    }
}

/**
 * Sets model selection if it exists in options
 * @private
 * @param {HTMLSelectElement} select - Select element
 * @param {string} modelId - Model ID to select
 */
function setModelIfExists(select, modelId) {
    const modelExists = Array.from(select.options).some(opt => opt.value === modelId);
    if (modelExists) {
        select.value = modelId;
        console.log(`Set ${select.id} to:`, modelId);
    } else {
        console.log(`Model not found in options:`, modelId);
    }
}

/**
 * Loads a selected model
 * @param {string} modelId - ID of model to load
 * @returns {Promise<void>}
 */
async function loadModel(modelId) {
    if (!modelId) return;

    try {
        MP.settings.model = modelId;
        const response = await new Promise((resolve, reject) => {
            genericRequest('LoadModelAsync', { modelId }, data => {
                if (data.success) {
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Failed to load model'));
                }
            });
        });
    } catch (error) {
        console.error('Error loading model:', error);
        showError(`Failed to load model: ${error.message}`);
    }
}

/**
 * Initializes settings modal with saved values
 * @private
 */
function initSettingsModal() {
    try {
        console.log('Initializing settings modal with:', MP.settings);
        
        // Set backend radios
        const backendMap = {
            'ollama': 'ollamaLLMBtn',
            'openrouter': 'openrouterLLMBtn',
            'openai': 'openaiLLMBtn',
            'openaiapi': 'openaiAPILLMBtn',
            'anthropic': 'anthropicLLMBtn'
        };

        // Set chat backend
        const chatBackendBtn = document.getElementById(backendMap[MP.settings.backend]);
        if (chatBackendBtn) {
            chatBackendBtn.checked = true;
        }

        // Set vision backend
        const visionBackendBtn = document.getElementById(backendMap[MP.settings.visionbackend]?.replace('LLM', 'Vision'));
        if (visionBackendBtn) {
            visionBackendBtn.checked = true;
        }

        // Set URLs
        const backendUrl = document.getElementById('backendUrl');
        if (backendUrl) {
            backendUrl.value = MP.settings.baseurl || '';
        }

        const visionBackendUrl = document.getElementById('visionBackendUrl');
        if (visionBackendUrl) {
            visionBackendUrl.value = MP.settings.visionbaseurl || '';
        }

        // Set API Key section
        const apiKeyBackendMap = {
            'ollama': 'ollamaKeyBtn',
            'openrouter': 'openrouterKeyBtn',
            'openai': 'openaiKeyBtn',
            'openaiapi': 'openaiAPIKeyBtn',
            'anthropic': 'anthropicApiBtn'
        };

        // Ensure instructions object exists
        if (!MP.settings.instructions) {
            MP.settings.instructions = {
                chat: '',
                vision: '',
                caption: ''
            };
        }
        // Set Instructions
        const chatInstructions = document.getElementById('chatInstructions');
        if (chatInstructions) {
            chatInstructions.value = MP.settings.instructions.chat || '';
        }
        const visionInstructions = document.getElementById('visionInstructions');
        if (visionInstructions) {
            visionInstructions.value = MP.settings.instructions.vision || '';
        }
        const captionInstructions = document.getElementById('captionInstructions');
        if (captionInstructions) {
            captionInstructions.value = MP.settings.instructions.caption || '';
        }

        // Add event listener for API key backend selection
        const apiKeyBackendRadios = document.querySelectorAll('input[name="apiKeyBackend"]');
        apiKeyBackendRadios.forEach(radio => {
            radio.addEventListener('change', updateApiKeyInput);
        });

        fetchModels();
    } catch (error) {
        console.error('Error initializing settings modal:', error);
        showError('Error initializing settings modal:', error);
    }
}

/**
 * Updates API key input when backend selection changes
 * @private
 */
function updateApiKeyInput() {
    const selectedBackend = document.querySelector('input[name="apiKeyBackend"]:checked');
    if (!selectedBackend) return;

    const backendToProvider = {
        'ollamaKeyBtn': 'ollama',
        'openrouterKeyBtn': 'openrouter',
        'openaiKeyBtn': 'openai',
        'openaiAPIKeyBtn': 'openaiapi',
        'anthropicApiBtn': 'anthropic'
    };

    const provider = backendToProvider[selectedBackend.id];
    const apiKeyInput = document.getElementById('apiKeyInput');
    if (apiKeyInput && provider) {
        apiKeyInput.value = MP.settings.backends?.[provider]?.apikey || '';
        apiKeyInput.placeholder = `Enter ${provider} API key`;
    }
}

/**
 * Saves API key for the selected backend
 */
function saveApiKey() {
    const selectedBackend = document.querySelector('input[name="apiKeyBackend"]:checked');
    const apiKeyInput = document.getElementById('apiKeyInput');
    if (!selectedBackend || !apiKeyInput) {
        showMessage('error', 'Please select a backend and enter an API key');
        showError('Please select a backend and enter an API key if applicible')
        return;
    }
    const backendToProvider = {
        'ollamaKeyBtn': 'ollama',
        'openrouterKeyBtn': 'openrouter',
        'openaiKeyBtn': 'openai',
        'openaiAPIKeyBtn': 'openaiapi',
        'anthropicApiBtn': 'anthropic'
    };
    const provider = backendToProvider[selectedBackend.id];
    if (!provider) {
        showError('Invalid backend selected')
        console.Error('Invalid backend selected')
        return;
    }
    // Update the settings object
    if (!MP.settings.backends) {
        MP.settings.backends = {};
    }
    if (!MP.settings.backends[provider]) {
        MP.settings.backends[provider] = {};
    }
    MP.settings.backends[provider].apikey = apiKeyInput.value;
    saveSettings();
}

/**
 * Closes the settings modal
 */
function closeSettingsModal() {
    const modal = document.getElementById('settingsModal');
    if (!modal) return;
    const modalInstance = bootstrap.Modal.getInstance(modal);
    if (modalInstance) {
        modalInstance.hide();
    }
    // Clean up backdrop and body classes
    const backdrop = document.querySelector('.modal-backdrop');
    if (backdrop) backdrop.remove();
    document.body.classList.remove('modal-open');
    document.body.style.overflow = '';
    document.body.style.paddingRight = '';
}

// Initialize on DOM load
document.addEventListener("DOMContentLoaded", async function () {
    try {
        if (MP.initialized) return;
        MP.initialized = true;
        // Initialize settings
        await loadSettings();
        // Add event listener for settings modal
        const settingsModal = document.getElementById('settingsModal');
        settingsModal.addEventListener('show.bs.modal', initSettingsModal);
        // Initialize models once
        await fetchModels();
        MP.modelsInitialized = true;
        // Initialize chat and vision handlers
        window.visionHandler?.initialize();
        window.chatHandler?.initialize();
        console.log('MagicPrompt initialization complete');
    } catch (error) {
        console.error('Error initializing MagicPrompt:', error);
        showError('Failed to initialize MagicPrompt: ' + error.message);
    }
});