/**
 * magicprompt.js
 * Core functionality and utilities for the MagicPrompt extension.
 */

'use strict';

// Initialize MagicPrompt global namespace if it doesn't exist
if (!window.MP) {
    window.MP = {
        initialized: false,
        settings: {
            // Core settings
            backend: 'ollama',
            model: '',
            visionbackend: 'ollama',
            visionmodel: '',
            linkChatAndVisionModels: true, // Default to true
            // Backend configurations
            backends: {
                ollama: {
                    baseurl: 'http://localhost:11434',
                    unloadModel: false,
                    endpoints: {
                        chat: '/api/chat',
                        models: '/api/tags'
                    }
                },
                openaiapi: {
                    baseurl: 'http://localhost:11434',
                    unloadModel: false,
                    endpoints: {
                        chat: 'v1/chat/completions',
                        models: '/v1/models'
                    },
                    apikey: ''
                },
                openai: {
                    baseurl: 'https://api.openai.com',
                    endpoints: {
                        chat: 'v1/chat/completions',
                        models: 'v1/models'
                    },
                    apikey: ''
                },
                anthropic: {
                    baseurl: 'https://api.anthropic.com',
                    endpoints: {
                        chat: 'v1/messages',
                        models: 'v1/models'
                    },
                    apikey: ''
                },
                openrouter: {
                    baseurl: 'https://openrouter.ai',
                    endpoints: {
                        chat: '/api/v1/chat/completions',
                        models: '/api/v1/models'
                    },
                    apikey: ''
                }
            },
            // Instructions
            instructions: {
                chat: '',
                vision: '',
                caption: '',
                prompt: ''
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
             * @param {string} action - Action type ('chat', 'vision', 'prompt', 'caption')
             * @returns {Object} Formatted request payload
             */
            createRequestPayload(input, image, action) {
                if (!input?.trim()) {
                    throw new Error('Input is required');
                }
                const hasImage = Boolean(image);
                try {
                    // Get model and backend based on request type
                    const modelId = this.getModelId(hasImage);
                    const backend = hasImage ? MP.settings.visionbackend : MP.settings.backend;
                    // Get appropriate instructions based on action type
                    const instructions = MP.settings.instructions?.[action.toLowerCase()];
                    // Create the message content
                    const messageContent = {
                        text: input,
                        media: image ? [{ type: "base64", data: image, mediaType: window.visionHandler?.currentMediaType || "image/jpeg" }] : null,
                        instructions: instructions,
                        KeepAlive: (backend.toLowerCase() === 'ollama' && MP.settings.backends[backend]?.unloadModel) ? 0 : null
                    };
                    return {
                        messageContent,
                        modelId,
                        messageType: hasImage ? "Vision" : "Text",
                        action: action.toLowerCase(),
                    };
                } catch (error) {
                    console.error('Error creating request payload:', error);
                    throw error;
                }
            },

            getModelId(isVision) {
                if (MP.settings.linkChatAndVisionModels) {
                    return document.getElementById('modelSelect')?.value;
                }
                const modelId = isVision
                    ? document.getElementById('visionModel')?.value
                    : document.getElementById('modelSelect')?.value;

                if (!modelId) {
                    throw new Error('Please select a model first');
                }
                return modelId;
            },
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
        },

        ResizeHandler: class {
            constructor(options) {
                this.handle = options.handle;
                this.panel = options.panel;
                this.storageKey = options.storageKey;
                this.defaultWidth = options.defaultWidth || 400;
                this.minWidth = options.minWidth || 300;
                this.maxWidthOffset = options.maxWidthOffset || 300;
                this.isDragging = false;
                this.startX = 0;
                this.startWidth = 0;
                // Bind methods
                this.startDragging = this.startDragging.bind(this);
                this.doDrag = this.doDrag.bind(this);
                this.stopDragging = this.stopDragging.bind(this);
                this.init();
            }

            init() {
                // Set initial width from storage or default
                const storedWidth = localStorage.getItem(this.storageKey);
                const initialWidth = storedWidth ? parseInt(storedWidth) : this.defaultWidth;
                this.panel.style.width = `${initialWidth}px`;
                // Add event listener to handle
                this.handle.addEventListener('mousedown', this.startDragging);
                // Add to layout resets if available
                if (window.layoutResets) {
                    window.layoutResets.push(() => {
                        localStorage.removeItem(this.storageKey);
                        this.panel.style.width = `${this.defaultWidth}px`;
                    });
                }
            }

            startDragging(e) {
                this.isDragging = true;
                this.startX = e.pageX;
                this.startWidth = parseInt(document.defaultView.getComputedStyle(this.panel).width, 10);
                // Add event listeners
                document.addEventListener('mousemove', this.doDrag);
                document.addEventListener('mouseup', this.stopDragging);
                // Add dragging class
                document.body.classList.add('dragging');
                // Prevent text selection
                e.preventDefault();
            }

            doDrag(e) {
                if (!this.isDragging) return;
                e.preventDefault();
                const containerWidth = this.panel.parentElement.getBoundingClientRect().width;
                // Calculate new width
                let newWidth = this.startWidth + (e.pageX - this.startX);
                // Clamp the width between min and max
                newWidth = Math.max(this.minWidth, Math.min(containerWidth - this.maxWidthOffset, newWidth));
                // Apply the new width
                this.panel.style.width = `${newWidth}px`;
                // Save the width
                localStorage.setItem(this.storageKey, newWidth);
            }

            stopDragging() {
                if (!this.isDragging) return;
                this.isDragging = false;
                document.removeEventListener('mousemove', this.doDrag);
                document.removeEventListener('mouseup', this.stopDragging);
                document.body.classList.remove('dragging');
            }
        }
    }
};

// Define backends that don't need base URL configuration
const FIXED_URL_BACKENDS = ['openai', 'anthropic', 'openrouter'];

// Helper function to check if a backend needs base URL configuration
function needsBaseUrl(backend) {
    return !FIXED_URL_BACKENDS.includes(backend);
}

// Updates base URL visibility based on selected backend
function updateBaseUrlVisibility(backend, isVision = false) {
    const containerId = isVision ? 'visionBaseUrlContainer' : 'baseUrlContainer';
    const container = document.getElementById(containerId);
    if (container) {
        container.style.display = needsBaseUrl(backend) ? 'block' : 'none';
    }
}

/**
 * Handles the enhance prompt button click
 * Takes the current prompt text and enhances it using the LLM
 */
async function handleEnhancePrompt() {
    const promptTextArea = document.getElementById('alt_prompt_textbox');
    if (!promptTextArea || !promptTextArea.value.trim()) {
        showError('Please enter a prompt to enhance');
        return;
    }
    if (window.isEnhancing) return;
    try {
        window.isEnhancing = true;
        const input = promptTextArea.value.trim();
        const payload = MP.RequestBuilder.createRequestPayload(
            input,
            null,
            'prompt'
        );
        const response = await MP.APIClient.makeRequest(payload);
        if (response.success && response.response) {
            promptTextArea.value = response.response;
            triggerChangeFor(promptTextArea);
            promptTextArea.focus();
            promptTextArea.setSelectionRange(0, promptTextArea.value.length);
        } else {
            throw new Error(response.error || 'Failed to enhance prompt');
        }
    } catch (error) {
        console.error('Prompt enhancement error:', error);
        showError(error.message);
    } finally {
        window.isEnhancing = false;
    }
}

/**
 * Handles the vision analysis button click
 * Analyzes the currently selected image using vision LLM
 */
async function handleVisionAnalysis() {
    const currentImage = document.querySelector('#current_image img.current-image-img');
    if (!currentImage?.src) {
        showError('No image selected');
        return;
    }
    try {
        // Fetch the image data and convert to base64
        const fetchResponse = await fetch(currentImage.src);
        const blob = await fetchResponse.blob();
        // Convert blob to base64
        const reader = new FileReader();
        const base64Data = await new Promise((resolve) => {
            reader.onloadend = () => {
                // Get just the base64 data without the data URL prefix
                resolve(reader.result.split(',')[1]);
            };
            reader.readAsDataURL(blob);
        });
        const payload = MP.RequestBuilder.createRequestPayload(
            MP.settings.instructions.caption,
            base64Data,
            'vision'
        );
        const response = await MP.APIClient.makeRequest(payload);
        if (response.success && response.response) {
            const promptBox = document.getElementById('alt_prompt_textbox');
            if (promptBox) {
                promptBox.value = response.response;
                triggerChangeFor(promptBox);
                promptBox.focus();
                promptBox.setSelectionRange(0, promptBox.value.length);
            }
        } else {
            throw new Error(response.error || 'Failed to analyze image');
        }
    } catch (error) {
        console.error('Vision analysis error:', error);
        showError(`Failed to analyze image. Have you selected a vision model in settings? Error: ${error.message}`);
    }
}

function addPromptButtons() {
    const altPromptRegion = document.querySelector('.alt_prompt_region');
    if (!altPromptRegion) return;
    // Create container
    const container = document.createElement('div');
    container.className = 'magicprompt prompt-buttons-container';
    // Create enhance button
    const enhanceButton = document.createElement('button');
    enhanceButton.className = 'magicprompt prompt-button';
    enhanceButton.innerHTML = '<i class="fas fa-wand-magic-sparkles"></i> 🪄 Enhance Prompt';
    enhanceButton.addEventListener('click', handleEnhancePrompt);
    // Create vision button
    const visionButton = document.createElement('button');
    visionButton.className = 'magicprompt prompt-button';
    visionButton.innerHTML = '<i class="fas fa-eye"></i> 👀 Magic Vision';
    visionButton.addEventListener('click', handleVisionAnalysis);
    // Add buttons to container
    container.appendChild(enhanceButton);
    container.appendChild(visionButton);
    // Insert container into the alt prompt region
    altPromptRegion.insertBefore(container, altPromptRegion.firstChild);
}

/**
 * Loads settings from the backend
 * @returns {Promise<Object>} Settings object
 * @throws {Error} If settings cannot be loaded
 */
async function loadSettings() {
    try {
        const response = await new Promise((resolve, reject) => {
            genericRequest('GetSettingsAsync', {}, data => {
                if (data.success) {
                    const serverSettings = data.settings;
                    // Create settings object preserving server values
                    const settings = {
                        // Core settings
                        backend: serverSettings.backend || 'ollama',
                        model: serverSettings.model || '',
                        visionbackend: serverSettings.visionbackend || serverSettings.backend || 'ollama',
                        visionmodel: serverSettings.visionmodel || '',
                        linkChatAndVisionModels: serverSettings.linkChatAndVisionModels !== false, // Default to true
                        // Backends - merge using spread operator which does a "deep merge" of two objects
                        backends: {
                            ...MP.settings.backends,  // Start with default endpoints
                            ...(serverSettings.backends || {}),  // Overlay server settings
                        },
                        instructions: {
                            chat: serverSettings.instructions?.chat || '',
                            vision: serverSettings.instructions?.vision || '',
                            caption: serverSettings.instructions?.caption || '',
                            prompt: serverSettings.instructions?.prompt || ''
                        }
                    };
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
        // Check if models are linked
        const isLinked = document.getElementById('linkModelsToggle')?.checked;
        
        // Get the selected backends
        const selectedChatBackend = document.querySelector('input[name="llmBackend"]:checked');
        const selectedVisionBackend = document.querySelector('input[name="visionBackendSelect"]:checked');
        
        // Extract backend IDs
        const chatBackendId = selectedChatBackend ? 
            selectedChatBackend.id.replace('LLMBtn', '').toLowerCase() : 
            MP.settings.backend;
            
        // If linked, use chat backend for vision too
        const visionBackendId = isLinked ? chatBackendId : 
            (selectedVisionBackend ? 
                selectedVisionBackend.id.replace('VisionBtn', '').toLowerCase() : 
                MP.settings.visionbackend);

        // Get models and use chat model for vision if linked
        const chatModel = document.getElementById('modelSelect')?.value || MP.settings.model;
        const visionModel = isLinked ? chatModel : 
            (document.getElementById('visionModel')?.value || MP.settings.visionmodel);

        // Get base URLs
        const chatBaseUrl = document.getElementById('backendBaseUrl')?.value || 
            MP.settings.backends[chatBackendId]?.baseurl;
        const visionBaseUrl = isLinked ? chatBaseUrl : 
            (document.getElementById('visionBackendBaseUrl')?.value || 
             MP.settings.backends[visionBackendId]?.baseurl);

        // Create settings object matching exact structure expected by C# DefaultSettings
        const settings = {
            // Core settings
            backend: chatBackendId,
            model: chatModel,
            visionbackend: visionBackendId,
            visionmodel: visionModel,
            linkChatAndVisionModels: isLinked,
            backends: {
                ...MP.settings.backends,
                [chatBackendId]: {
                    ...MP.settings.backends[chatBackendId],
                    baseurl: chatBaseUrl,
                    unloadmodel: document.getElementById('unload_models_toggle')?.checked,
                },
                [visionBackendId]: {
                    ...MP.settings.backends[visionBackendId],
                    baseurl: visionBaseUrl,
                    unloadmodel: document.getElementById('unload_models_toggle')?.checked,
                }
            },
            instructions: {
                chat: document.getElementById('chatInstructions')?.value || MP.settings.instructions?.chat || '',
                vision: document.getElementById('visionInstructions')?.value || MP.settings.instructions?.vision || '',
                caption: document.getElementById('captionInstructions')?.value || MP.settings.instructions?.caption || '',
                prompt: document.getElementById('promptInstructions')?.value || MP.settings.instructions?.prompt || ''
            }
        };

        // Update MP.settings with the new values
        MP.settings = settings;
        
        // Create request payload with settings properly wrapped
        const payload = { 
            settings 
        };

        // Use genericRequest which will automatically add session_id
        genericRequest('SaveSettingsAsync', payload, 
            (data) => {
                if (data.success) {
                    console.log('Settings saved successfully');
                } else {
                    console.error(`Failed to save settings: ${data.error || 'Unknown error'}`);
                    showError(`Failed to save settings: ${data.error || 'Unknown error'}`);
                }
            },
            0,  // depth
            (error) => {
                console.error('Error saving settings:', error);
                showError(`Error saving settings: ${error}`);
            }
        );
    } catch (error) {
        console.error('Error in saveSettings:', error);
        showError(`Error in saveSettings: ${error.message}`);
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
        document.getElementById('promptInstructions').value = '';
        closeSettingsModal()
    } catch (error) {
        console.error('Settings reset error:', error);
        showError(`Failed to reset settings: ${error.message}`);
    }
}

/**
 * Fetches available models from the backend
 * @returns {Promise<Object>} Object containing the fetched models
 */
async function fetchModels() {
    const modelSelect = document.getElementById("modelSelect");
    const visionModelSelect = document.getElementById("visionModel");

    if (!modelSelect || !visionModelSelect) {
        console.error('Model select elements not found');
        return Promise.reject(new Error('Model select elements not found'));
    }
    
    // Create a request semaphore
    if (MP.fetchingModels) {
        console.log('Models already being fetched, waiting for current request to complete');
        return MP.fetchingModelsPromise;
    }
    
    try {
        // Set semaphore
        MP.fetchingModels = true;
        MP.fetchingModelsPromise = (async () => {
            // Use the currently set backend values from MP.settings
            // These will be temporarily set by the radio button handlers
            const chatBackendId = MP.settings.backend || 'ollama';
            const visionBackendId = MP.settings.visionbackend || 'ollama';
            
            console.log(`Fetching models for chat backend: ${chatBackendId}, vision backend: ${visionBackendId}`);
            
            // Fetch models for both backends
            const response = await new Promise((resolve, reject) => {
                genericRequest('GetModelsAsync', {
                    backend: chatBackendId,
                    visionbackend: visionBackendId,
                    // Include baseUrl information if available
                    backends: {
                        [chatBackendId]: {
                            baseurl: MP.settings.backends[chatBackendId]?.baseurl || '',
                        },
                        [visionBackendId]: {
                            baseurl: MP.settings.backends[visionBackendId]?.baseurl || '',
                        }
                    }
                }, data => {
                    if (data.success) {
                        resolve(data);
                    } else {
                        reject(new Error(data.error || 'Failed to fetch models'));
                    }
                });
            });
            // Add chat models
            if (Array.isArray(response.models)) {
                const existingModelIds = new Set();
                
                // Clear existing options if not already cleared
                if (modelSelect.options.length > 0) {
                    modelSelect.innerHTML = '';
                    // Add default option
                    const defaultOption = new Option('-- Select a model --', '');
                    modelSelect.add(defaultOption);
                }
                
                response.models.forEach(model => {
                    if (!model.model || existingModelIds.has(model.model)) {
                        if (!model.model) {
                            console.warn('Chat model missing model field:', model);
                        }
                        return;
                    }
                    
                    existingModelIds.add(model.model);
                    const option = new Option(model.name || model.model, model.model);
                    modelSelect.add(option.cloneNode(true));
                });
                if (MP.settings.model) {
                    setModelIfExists(modelSelect, MP.settings.model);
                }
            }
            // Add vision models
            if (Array.isArray(response.visionmodels)) {
                const existingVisionModelIds = new Set();
                
                // Clear existing options if not already cleared
                if (visionModelSelect.options.length > 0) {
                    visionModelSelect.innerHTML = '';
                    // Add default option
                    const defaultOption = new Option('-- Select a vision model --', '');
                    visionModelSelect.add(defaultOption);
                }
                
                response.visionmodels.forEach(model => {
                    if (!model.model || existingVisionModelIds.has(model.model)) {
                        if (!model.model) {
                            console.warn('Vision model missing model field:', model);
                        }
                        return;
                    }
                    
                    existingVisionModelIds.add(model.model);
                    const option = new Option(model.name || model.model, model.model);
                    visionModelSelect.add(option);
                });
                if (MP.settings.visionmodel) {
                    setModelIfExists(visionModelSelect, MP.settings.visionmodel);
                }
            }
            
            console.log(`Models populated - Chat: ${modelSelect.options.length - 1}, Vision: ${visionModelSelect.options.length - 1}`);
            // Return the fetched data for debugging
            return response;
        })();
        
        await MP.fetchingModelsPromise;
        return MP.fetchingModelsPromise;
    } catch (error) {
        console.error('Error fetching models:', error);
        showError(`Failed to fetch models: ${error.message}`);
        return Promise.reject(error);
    } finally {
        // Clear semaphore
        MP.fetchingModels = false;
        MP.fetchingModelsPromise = null;
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
    }
}

/**
 * Initializes settings modal with saved values
 * @private
 */
function initSettingsModal() {
    try {
        // Initialize the linked models state
        const isLinked = MP.settings.linkChatAndVisionModels !== false; // Default to true if not set
        
        // Set the toggle to match the stored setting
        const linkModelsToggle = document.getElementById('linkModelsToggle');
        if (linkModelsToggle) {
            linkModelsToggle.checked = isLinked;
            updateLinkedModelsUI(isLinked);
        }
        
        // Select the correct backend based on current settings
        const currentBackend = MP.settings.backend || 'ollama';
        const currentBackendRadio = document.getElementById(`${currentBackend}LLMBtn`);
        if (currentBackendRadio) {
            currentBackendRadio.checked = true;
        }
        updateBaseUrlVisibility(currentBackend, false);

        // Set base URL if needed
        const backendUrl = document.getElementById('backendBaseUrl');
        if (backendUrl) {
            backendUrl.value = MP.settings.backends[currentBackend]?.baseurl || '';
        }

        // Select the correct vision backend based on current settings
        const currentVisionBackend = MP.settings.visionbackend || 'ollama';
        const currentVisionBackendRadio = document.getElementById(`${currentVisionBackend}VisionBtn`);
        if (currentVisionBackendRadio) {
            currentVisionBackendRadio.checked = true;
        }
        updateBaseUrlVisibility(currentVisionBackend, true);

        // Set vision base URL if needed
        const visionBackendUrl = document.getElementById('visionBackendBaseUrl');
        if (visionBackendUrl) {
            visionBackendUrl.value = MP.settings.backends[currentVisionBackend]?.baseurl || '';
        }
        
        // Ensure instructions object exists
        if (!MP.settings.instructions) {
            MP.settings.instructions = {
                chat: '',
                vision: '',
                caption: '',
                prompt: ''
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
        const promptInstructions = document.getElementById('promptInstructions');
        if (promptInstructions) {
            promptInstructions.value = MP.settings.instructions.prompt || '';
        }
        
        // Show loading state before fetching models
        const modelSelect = document.getElementById("modelSelect");
        const visionModelSelect = document.getElementById("visionModel");
        
        // Use the already defined backend variables from above
        if (modelSelect) {
            modelSelect.innerHTML = '';
            const loadingOption = new Option(`Loading ${currentBackend} models...`, '');
            loadingOption.disabled = true;
            modelSelect.add(loadingOption);
        }
        if (visionModelSelect) {
            visionModelSelect.innerHTML = '';
            const loadingOption = new Option(`Loading ${currentVisionBackend} vision models...`, '');
            loadingOption.disabled = true;
            visionModelSelect.add(loadingOption);
        }
        
        // Fetch models for the selected backends
        console.log(`Fetching initial models for ${currentBackend} and ${currentVisionBackend} backends`);
        fetchModels().then(result => {
            console.log('Initial models loaded:', result);
        }).catch(error => {
            console.error('Error loading initial models:', error);
            // Show error options if there's a failure
            if (modelSelect) {
                modelSelect.innerHTML = '';
                const errorOption = new Option('Error loading models', '');
                errorOption.disabled = true;
                modelSelect.add(errorOption);
            }
            if (visionModelSelect) {
                visionModelSelect.innerHTML = '';
                const errorOption = new Option('Error loading vision models', '');
                errorOption.disabled = true;
                visionModelSelect.add(errorOption);
            }
        });
        
        // Add event listeners for backend selection
        const linkingBackendRadios = document.querySelectorAll('input[name="llmBackend"]');
        linkingBackendRadios.forEach(radio => {
            radio.addEventListener('change', async (e) => {
                const backend = e.target.id.replace('LLMBtn', '').toLowerCase();
                
                // Update UI for base URL input
                updateBaseUrlVisibility(backend, false);
                
                // Show loading state in the model dropdown
                const modelSelect = document.getElementById("modelSelect");
                if (modelSelect) {
                    modelSelect.innerHTML = '';
                    const loadingOption = new Option(`Loading ${backend} models...`, '');
                    loadingOption.disabled = true;
                    modelSelect.add(loadingOption);
                }
                
                try {
                    // Get current base URL
                    const backendUrl = document.getElementById('backendBaseUrl');
                    const baseUrl = backendUrl ? backendUrl.value : '';
                    
                    // Update MP.settings directly
                    MP.settings.backend = backend;
                    
                    // Update baseurl if needed
                    if (baseUrl && needsBaseUrl(backend)) {
                        if (!MP.settings.backends[backend]) {
                            MP.settings.backends[backend] = {};
                        }
                        MP.settings.backends[backend].baseurl = baseUrl;
                    }
                    
                    // Save settings with minimal data (just backend)
                    const payload = { 
                        settings: {
                            backend: backend,
                            backends: {
                                ...MP.settings.backends
                            }
                        } 
                    };

                    // Use genericRequest which will automatically add session_id
                    await new Promise((resolve, reject) => {
                        genericRequest('SaveSettingsAsync', payload, 
                            (data) => {
                                if (data.success) {
                                    resolve();
                                } else {
                                    reject(new Error(data.error || 'Failed to save settings'));
                                }
                            },
                            0,
                            (error) => reject(error)
                        );
                    });
                    
                    // Fetch models with the updated settings
                    await fetchModels();
                } catch (error) {
                    console.error(`Error fetching models for ${backend}:`, error);
                    if (modelSelect) {
                        modelSelect.innerHTML = '';
                        const errorOption = new Option(`Error loading models: ${error.message}`, '');
                        errorOption.disabled = true;
                        modelSelect.add(errorOption);
                    }
                }
            });
        });

        // Add event listeners for vision backend selection
        const visionBackendRadios = document.querySelectorAll('input[name="visionBackendSelect"]');
        visionBackendRadios.forEach(radio => {
            radio.addEventListener('change', async (e) => {
                const backend = e.target.id.replace('VisionBtn', '').toLowerCase();
                
                // Update UI for base URL input
                updateBaseUrlVisibility(backend, true);
                
                // Show loading state in the model dropdown
                const modelSelect = document.getElementById("visionModel");
                if (modelSelect) {
                    modelSelect.innerHTML = '';
                    const loadingOption = new Option(`Loading ${backend} vision models...`, '');
                    loadingOption.disabled = true;
                    modelSelect.add(loadingOption);
                }
                
                try {
                    // Get current base URL
                    const backendUrl = document.getElementById('visionBackendBaseUrl');
                    const baseUrl = backendUrl ? backendUrl.value : '';
                    
                    // Update MP.settings directly
                    MP.settings.visionbackend = backend;
                    
                    // Update baseurl if needed
                    if (baseUrl && needsBaseUrl(backend)) {
                        if (!MP.settings.backends[backend]) {
                            MP.settings.backends[backend] = {};
                        }
                        MP.settings.backends[backend].baseurl = baseUrl;
                    }
                    
                    // Save settings with minimal data (just vision backend)
                    const payload = { 
                        settings: {
                            visionbackend: backend,
                            backends: {
                                ...MP.settings.backends
                            }
                        } 
                    };
                    
                    // Save settings
                    console.log(`Saving vision backend settings for ${backend}`);
                    
                    // Use genericRequest which will automatically add session_id
                    await new Promise((resolve, reject) => {
                        genericRequest('SaveSettingsAsync', payload, 
                            (data) => {
                                if (data.success) {
                                    resolve();
                                } else {
                                    reject(new Error(data.error || 'Failed to save settings'));
                                }
                            },
                            0,
                            (error) => reject(error)
                        );
                    });
                    
                    // Fetch models with the updated settings
                    await fetchModels();
                } catch (error) {
                    console.error(`Error fetching vision models for ${backend}:`, error);
                    if (modelSelect) {
                        modelSelect.innerHTML = '';
                        const errorOption = new Option(`Error loading vision models: ${error.message}`, '');
                        errorOption.disabled = true;
                        modelSelect.add(errorOption);
                    }
                }
            });
        });
        
        // Add event listeners for base URL changes
        const backendBaseUrl = document.getElementById('backendBaseUrl');
        if (backendBaseUrl) {
            backendBaseUrl.addEventListener('input', function(e) {
                // Sync with vision URL when linked
                if (document.getElementById('linkModelsToggle')?.checked) {
                    const visionBackendUrl = document.getElementById('visionBackendBaseUrl');
                    if (visionBackendUrl) {
                        visionBackendUrl.value = e.target.value;
                    }
                }
                
                // Original functionality to update models when URL changes
                const currentBackend = document.querySelector('input[name="llmBackend"]:checked')?.id.replace('LLMBtn', '').toLowerCase() || MP.settings.backend;
                if (needsBaseUrl(currentBackend)) {
                    // Clear and show loading state
                    const modelSelect = document.getElementById("modelSelect");
                    if (modelSelect) {
                        modelSelect.innerHTML = '';
                        const loadingOption = new Option(`Loading ${currentBackend} models with new URL...`, '');
                        loadingOption.disabled = true;
                        modelSelect.add(loadingOption);
                        
                        // Temporarily update settings for API call
                        const originalBaseUrl = MP.settings.backends[currentBackend]?.baseurl;
                        if (!MP.settings.backends[currentBackend]) {
                            MP.settings.backends[currentBackend] = {};
                        }
                        MP.settings.backends[currentBackend].baseurl = e.target.value;
                        
                        // Fetch models with new base URL
                        console.log(`Fetching models for ${currentBackend} with new base URL: ${e.target.value}`);
                        fetchModels().then(result => {
                            console.log(`Models fetched with new base URL:`, result);
                        }).catch(error => {
                            console.error(`Error fetching models with new base URL:`, error);
                            modelSelect.innerHTML = '';
                            const errorOption = new Option('Error loading models with new URL', '');
                            errorOption.disabled = true;
                            modelSelect.add(errorOption);
                        }).finally(() => {
                            // Restore original setting if user hasn't saved
                            MP.settings.backends[currentBackend].baseurl = originalBaseUrl;
                        });
                    }
                }
            });
        }
        
        // Add event listeners for vision base URL changes
        const visionBackendUrlInput = document.getElementById('visionBackendBaseUrl');
        if (visionBackendUrlInput) {
            visionBackendUrlInput.addEventListener('input', function(e) {
                const currentVisionBackend = document.querySelector('input[name="visionBackendSelect"]:checked')?.id.replace('VisionBtn', '').toLowerCase() || MP.settings.visionbackend;
                if (needsBaseUrl(currentVisionBackend)) {
                    // Clear and show loading state
                    const visionModelSelect = document.getElementById("visionModel");
                    if (visionModelSelect) {
                        visionModelSelect.innerHTML = '';
                        const loadingOption = new Option(`Loading ${currentVisionBackend} vision models with new URL...`, '');
                        loadingOption.disabled = true;
                        visionModelSelect.add(loadingOption);
                        
                        // Temporarily update settings for API call
                        const originalBaseUrl = MP.settings.backends[currentVisionBackend]?.baseurl;
                        if (!MP.settings.backends[currentVisionBackend]) {
                            MP.settings.backends[currentVisionBackend] = {};
                        }
                        MP.settings.backends[currentVisionBackend].baseurl = e.target.value;
                        
                        // Fetch models with new base URL
                        console.log(`Fetching vision models for ${currentVisionBackend} with new base URL: ${e.target.value}`);
                        fetchModels().then(result => {
                            console.log(`Vision models fetched with new base URL:`, result);
                        }).catch(error => {
                            console.error(`Error fetching vision models with new base URL:`, error);
                            visionModelSelect.innerHTML = '';
                            const errorOption = new Option('Error loading vision models with new URL', '');
                            errorOption.disabled = true;
                            visionModelSelect.add(errorOption);
                        }).finally(() => {
                            // Restore original setting if user hasn't saved
                            MP.settings.backends[currentVisionBackend].baseurl = originalBaseUrl;
                        });
                    }
                }
            });
        }
        
        // Add event listener for link models toggle
        const toggleBtn = document.getElementById('linkModelsToggle');
        if (toggleBtn) {
            toggleBtn.addEventListener('change', function(e) {
                const isLinked = e.target.checked;
                updateLinkedModelsUI(isLinked);
            });
        }
        
        // Add event listener for model select change
        const modelDropdown = document.getElementById("modelSelect");
        if (modelDropdown) {
            modelDropdown.addEventListener('change', function(e) {
                if (document.getElementById('linkModelsToggle')?.checked) {
                    const visionModelSelect = document.getElementById("visionModel");
                    if (visionModelSelect) {
                        visionModelSelect.value = modelDropdown.value;
                    }
                }
            });
        }
        
        // Add event listeners for backend selection
        const linkBackendRadios = document.querySelectorAll('input[name="llmBackend"]');
        linkBackendRadios.forEach(radio => {
            radio.addEventListener('change', function(e) {
                if (document.getElementById('linkModelsToggle')?.checked) {
                    const backend = e.target.id.replace('LLMBtn', '').toLowerCase();
                    const visionBackendRadio = document.getElementById(`${backend}VisionBtn`);
                    if (visionBackendRadio && !visionBackendRadio.checked) {
                        visionBackendRadio.checked = true;
                        const changeEvent = new Event('change');
                        visionBackendRadio.dispatchEvent(changeEvent);
                    }
                }
            });
        });
    } catch (error) {
        console.error('Error initializing settings modal:', error);
        showError('Error initializing settings modal:', error);
    }
}

// Add initSettingsModal to global scope for other scripts
window.initSettingsModal = initSettingsModal;
MP.initSettingsModal = initSettingsModal;

/**
 * Initializes on DOM load
 */
document.addEventListener("DOMContentLoaded", async function () {
    try {
        if (MP.initialized) return;
        MP.initialized = true;
        // Initialize settings
        await loadSettings();
        // Add prompt buttons
        addPromptButtons();
        // Initialize modal
        $('#settingsModal').modal({backdrop: 'static', keyboard: false, show: false
        }).on('show.bs.modal', initSettingsModal);
        // Initialize models
        await fetchModels();
        MP.modelsInitialized = true;
        // Initialize handlers
        if (window.visionHandler) {
            await window.visionHandler.initialize();
        }
        if (window.chatHandler) {
            await window.chatHandler.initialize();
        }
        // Initialize resize handler
        const resizeHandle = document.getElementById('resize_handle');
        const visionSection = document.getElementById('vision_section');
        if (resizeHandle && visionSection) {
            new MP.ResizeHandler({
                handle: resizeHandle,
                panel: visionSection,
                storageKey: 'magicprompt_vision_width',
                defaultWidth: 400,
                minWidth: 300,
                maxWidthOffset: 300
            });
        }
        // Update linked models UI
        const isLinked = MP.settings.linkChatAndVisionModels !== false;
        updateLinkedModelsUI(isLinked);
    } catch (error) {
        console.error('Error initializing MagicPrompt:', error);
        MP.ResponseHandler.showError('Failed to initialize MagicPrompt: ' + error.message);
    }
});

function updateLinkedModelsUI(isLinked) {
    try {
        // Get UI elements
        const visionSettingsCard = document.querySelector('.settings-card:nth-child(2)');
        const chatSettingsCard = document.querySelector('.settings-card:first-child');
        const visionContent = document.getElementById('visionSettingsCollapse');
        const chatContent = document.getElementById('chatSettingsCollapse');
        const visionHeader = visionSettingsCard?.querySelector('.settings-section-title');
        const chatHeader = chatSettingsCard?.querySelector('.settings-section-title');
        
        // If elements don't exist, exit early
        if (!visionSettingsCard || !chatSettingsCard) return;
        
        // Get model selects
        const chatModelSelect = document.getElementById("modelSelect");
        const visionModelSelect = document.getElementById("visionModel");
        
        if (isLinked) {
            // Update chat settings header to indicate it's linked
            if (chatHeader) {
                chatHeader.textContent = 'Chat and Vision Settings (Linked)';
            }
            
            // Hide vision settings section completely
            visionSettingsCard.style.display = 'none';
            
            // Make chat settings always expanded
            if (chatContent && !chatContent.classList.contains('show')) {
                chatContent.classList.add('show');
                const button = chatSettingsCard.querySelector('.collapse-toggle');
                if (button) button.setAttribute('aria-expanded', 'true');
            }
            
            // Sync vision model with chat model
            if (chatModelSelect && visionModelSelect) {
                visionModelSelect.value = chatModelSelect.value;
            }
            
            // Sync vision backend with chat backend
            const selectedChatBackend = document.querySelector('input[name="llmBackend"]:checked');
            if (selectedChatBackend) {
                const chatBackendId = selectedChatBackend.id.replace('LLMBtn', '').toLowerCase();
                const visionBackendRadio = document.getElementById(`${chatBackendId}VisionBtn`);
                if (visionBackendRadio) {
                    visionBackendRadio.checked = true;
                }
            }
            
            // Sync base URLs
            const backendUrl = document.getElementById('backendBaseUrl');
            const visionBackendUrl = document.getElementById('visionBackendBaseUrl');
            if (backendUrl && visionBackendUrl) {
                visionBackendUrl.value = backendUrl.value;
            }
        } else {
            // Restore chat title
            if (chatHeader) {
                chatHeader.textContent = 'Chat LLM Settings';
            }
            
            // Show vision settings
            visionSettingsCard.style.display = 'block';
        }
    } catch (error) {
        console.error('Error updating linked models UI:', error);
    }
}

/**
 * Closes the settings modal
 */
function closeSettingsModal() {
    try {
        $('#settingsModal').modal('hide');
        $('.modal-backdrop').remove();
        $('body').removeClass('modal-open').css('padding-right', '');
    } catch (error) {
        console.error('Error closing settings modal:', error);
    }
}
