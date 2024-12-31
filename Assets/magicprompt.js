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
                const chatMode = document.getElementById('chat_mode')?.checked;
                const visionMode = document.getElementById('vision_mode')?.checked;
                const promptMode = document.getElementById('prompt_mode')?.checked;
                // Get the appropriate instructions based on action type
                const getInstructionsForAction = (action) => {
                    const instructions = MP.settings.instructions || {};
                    switch (action.toLowerCase()) {
                        case 'chat':
                            return instructions.chat;
                        case 'vision':
                            return instructions.vision;
                        case 'prompt':
                            return instructions.prompt;
                        case 'caption':
                            return instructions.caption;
                        default:
                            return null;
                    }
                };
                try {
                    // Get model and backend based on request type
                    const modelId = this.getModelId(hasImage);
                    const backend = hasImage ? MP.settings.visionbackend : MP.settings.backend;
                    // Get appropriate instructions for this action
                    const instructions = getInstructionsForAction(action);
                    // Just use the input text directly, instructions will be sent separately
                    const text = input;
                    // Create the message content based on the action type
                    const messageContent = {
                        text,
                        media: image ? [{
                            type: "base64",
                            data: image,
                            mediaType: window.visionHandler?.currentMediaType || "image/jpeg"
                        }] : null
                    };
                    const keepAlive = (backend.toLowerCase() === 'ollama' && MP.settings.unloadmodel) ? 0 : null;
                    // Add instructions based on action type
                    if (action.toLowerCase() === 'prompt') {
                        messageContent.systemPrompt = instructions;
                    } else {
                        messageContent.instructions = instructions;
                    }
                    return {
                        messageContent,
                        modelId,
                        messageType: hasImage ? "Vision" : "Text",
                        action: action.toLowerCase(), // Ensure consistent casing
                        keep_alive: keepAlive
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

// Use existing MP or create new reference
window.MP = window.MP || window.MagicPrompt;

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
    try {
        // Create request payload using built-in RequestBuilder
        const payload = MP.RequestBuilder.createRequestPayload(
            promptTextArea.value,
            null,
            'prompt'  // Using prompt instructions
        );
        const response = await MP.APIClient.makeRequest(payload);
        if (response.success && response.response) {
            promptTextArea.value = response.response;
            triggerChangeFor(promptTextArea);
            promptTextArea.focus();
            promptTextArea.setSelectionRange(0, promptTextArea.value.length);
        } else {
            throw new Error(response.error || 'Failed to enhance prompt. Do you have a model loaded?');
        }
    } catch (error) {
        console.error('Prompt enhancement error:', error);
        showError(`Failed to enhance prompt.Do you have a model loaded? Error: ${error.message}`);
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
            MP.settings.instructions.vision, // TODO: Using vision instructions. Should this be prompt?
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
        // Prevent any form submission
        event?.preventDefault();

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
        // Match exact structure expected by C# DefaultSettings
        const settings = {
            backend: selectedChatBackend ? backendMap[selectedChatBackend.id] : MP.settings.backend,
            model: chatModel || MP.settings.model,
            visionbackend: selectedVisionBackend ? backendMap[selectedVisionBackend.id.replace('VisionBtn', 'LLMBtn')] : MP.settings.visionbackend,
            visionmodel: visionModel || MP.settings.visionmodel,
            unloadmodel: document.getElementById('unload_models_toggle')?.checked,
            backends: {
                ...MP.settings.backends,
                [backendMap[selectedChatBackend.id]]: {
                    ...MP.settings.backends[backendMap[selectedChatBackend.id]],
                    baseurl: document.getElementById('backendUrl')?.value || MP.settings.backends[backendMap[selectedChatBackend.id]]?.baseurl
                },
                [backendMap[selectedVisionBackend.id.replace('VisionBtn', 'LLMBtn')]]: {
                    ...MP.settings.backends[backendMap[selectedVisionBackend.id.replace('VisionBtn', 'LLMBtn')]],
                    baseurl: document.getElementById('visionBackendUrl')?.value || MP.settings.backends[backendMap[selectedVisionBackend.id.replace('VisionBtn', 'LLMBtn')]]?.baseurl
                }
            },
            instructions: {
                chat: document.getElementById('chatInstructions')?.value || MP.settings.instructions?.chat || '',
                vision: document.getElementById('visionInstructions')?.value || MP.settings.instructions?.vision || '',
                caption: document.getElementById('captionInstructions')?.value || MP.settings.instructions?.caption || '',
                prompt: document.getElementById('promptInstructions')?.value || MP.settings.instructions?.prompt || ''
            }
        };
        const response = await new Promise((resolve, reject) => {
            genericRequest('SaveSettingsAsync', { settings }, data => {
                if (data.success) {
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Failed to save settings'));
                }
            });
        });
        // Update local settings with a deep copy
        MP.settings = JSON.parse(JSON.stringify(response.settings));
        
        // Refresh models and show any errors
        try {
            await fetchModels();
        } catch (error) {
            console.error('Failed to fetch models:', error);
            showError(`Failed to fetch models: ${error.message}`);
        }
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
        document.getElementById('promptInstructions').value = '';
        closeSettingsModal()
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
    try {
        // Clear existing options
        modelSelect.innerHTML = '';
        visionModelSelect.innerHTML = '';
        // Add default options
        const defaultOption = new Option('-- Select a model --', '');
        modelSelect.add(defaultOption.cloneNode(true));
        visionModelSelect.add(defaultOption.cloneNode(true));
        // Fetch models for both backends
        const response = await new Promise((resolve, reject) => {
            genericRequest('GetModelsAsync', {}, data => {
                if (data.success) {
                    resolve(data);
                } else {
                    reject(new Error(data.error || 'Failed to fetch models'));
                }
            });
        });
        // Add chat models
        if (Array.isArray(response.models)) {
            response.models.forEach(model => {
                if (!model.model) {
                    console.warn('Chat model missing model field:', model);
                    return;
                }
                const option = new Option(model.name || model.model, model.model);
                modelSelect.add(option);
            });
            if (MP.settings.model) {
                setModelIfExists(modelSelect, MP.settings.model);
            }
        }
        // Add vision models
        if (Array.isArray(response.visionmodels)) {
            response.visionmodels.forEach(model => {
                if (!model.model) {
                    console.warn('Vision model missing model field:', model);
                    return;
                }
                const option = new Option(model.name || model.model, model.model);
                visionModelSelect.add(option);
            });
            if (MP.settings.visionmodel) {
                setModelIfExists(visionModelSelect, MP.settings.visionmodel);
            }
        }
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
            // Update base URL visibility for chat backend
            updateBaseUrlVisibility(MP.settings.backend, false);
        }
        // Set vision backend
        const visionBackendBtn = document.getElementById(backendMap[MP.settings.visionbackend]?.replace('LLM', 'Vision'));
        if (visionBackendBtn) {
            visionBackendBtn.checked = true;
            // Update base URL visibility for vision backend
            updateBaseUrlVisibility(MP.settings.visionbackend, true);
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
        // Add event listener for API key backend selection
        const apiKeyBackendRadios = document.querySelectorAll('input[name="apiKeyBackend"]');
        apiKeyBackendRadios.forEach(radio => {
            radio.addEventListener('change', updateApiKeyInput);
        });
        fetchModels();
        // Add event listeners for backend selection
        const backendRadios = document.querySelectorAll('input[name="llmBackend"]');
        backendRadios.forEach(radio => {
            radio.addEventListener('change', (e) => {
                const backend = e.target.id.replace('LLMBtn', '').toLowerCase();
                updateBaseUrlVisibility(backend, false);
            });
        });

        // Add event listeners for vision backend selection
        const visionBackendRadios = document.querySelectorAll('input[name="visionBackendSelect"]');
        visionBackendRadios.forEach(radio => {
            radio.addEventListener('change', (e) => {
                const backend = e.target.id.replace('VisionBtn', '').toLowerCase();
                updateBaseUrlVisibility(backend, true);
            });
        });

        // Set API Key section
        const apiKeyInput = document.getElementById('apiKeyInput');
        if (apiKeyInput) {
            apiKeyInput.value = MP.settings.backends?.[MP.settings.backend]?.apikey || '';
            apiKeyInput.placeholder = `Enter ${MP.settings.backend} API key`;
        }
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
    try {
        $('#settingsModal').modal('hide');
        $('.modal-backdrop').remove();
        $('body').removeClass('modal-open').css('padding-right', '');
    } catch (error) {
        console.error('Error closing settings modal:', error);
    }
}

// Initialize on DOM load
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
    } catch (error) {
        console.error('Error initializing MagicPrompt:', error);
        MP.ResponseHandler.showError('Failed to initialize MagicPrompt: ' + error.message);
    }
});