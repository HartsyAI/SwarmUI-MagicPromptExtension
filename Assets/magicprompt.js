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
                        chat: 'v1/messages'
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
        // Get the selected backends
        const selectedChatBackend = document.querySelector('input[name="llmBackend"]:checked');
        const selectedVisionBackend = document.querySelector('input[name="visionBackendSelect"]:checked');
        
        // Extract backend IDs
        const chatBackendId = selectedChatBackend ? 
            selectedChatBackend.id.replace('LLMBtn', '').toLowerCase() : 
            MP.settings.backend;
            
        const visionBackendId = selectedVisionBackend ? 
            selectedVisionBackend.id.replace('VisionBtn', '').toLowerCase() : 
            MP.settings.visionbackend;

        // Create settings object matching exact structure expected by C# DefaultSettings
        const settings = {
            // Core settings
            backend: chatBackendId,
            model: document.getElementById('modelSelect')?.value || MP.settings.model,
            visionbackend: visionBackendId,
            visionmodel: document.getElementById('visionModel')?.value || MP.settings.visionmodel,
            backends: {
                ...MP.settings.backends,
                [chatBackendId]: {
                    ...MP.settings.backends[chatBackendId],
                    baseurl: document.getElementById('backendBaseUrl')?.value || MP.settings.backends[chatBackendId]?.baseurl,
                    unloadmodel: document.getElementById('unload_models_toggle')?.checked,
                },
                [visionBackendId]: {
                    ...MP.settings.backends[visionBackendId],
                    baseurl: document.getElementById('visionBackendBaseUrl')?.value || MP.settings.backends[visionBackendId]?.baseurl,
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
        const payload = { settings };

        // Use genericRequest which will automatically add session_id
        genericRequest('SaveSettingsAsync', payload, 
            (data) => {
                if (data.success) {
                    showMessage('success', 'Settings saved successfully');
                } else {
                    showMessage('error', `Failed to save settings: ${data.error || 'Unknown error'}`);
                    showError(`Failed to save settings: ${data.error || 'Unknown error'}`);
                }
            },
            0,  // depth
            (error) => {
                console.error('Error saving settings:', error);
                showMessage('error', `Error saving settings: ${error}`);
                showError(`Error saving settings: ${error}`);
            }
        );
    } catch (error) {
        console.error('Error in saveSettings:', error);
        showMessage('error', `Error in saveSettings: ${error.message}`);
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
 * Initializes settings modal with saved values
 * @private
 */
function initSettingsModal() {
    try {
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
        
        // Update API Key section - now managed in User tab
        const apiKeyInput = document.getElementById('apiKeyInput');
        if (apiKeyInput) {
            apiKeyInput.value = '';
            apiKeyInput.placeholder = 'API keys are now managed in the User tab';
            apiKeyInput.disabled = true;
        }
        
        // Add information about API key management to the settings modal
        const apiKeySection = document.querySelector('.tab-pane[data-tab="api-key"]');
        if (apiKeySection) {
            // Check if notice box already exists to avoid duplicates
            if (!apiKeySection.querySelector('.notice-box')) {
                const infoDiv = document.createElement('div');
                infoDiv.className = 'notice-box';
                infoDiv.innerHTML = `
                    <p><strong>API Keys are now managed in the User tab</strong></p>
                    <p>To set your API keys:</p>
                    <ol>
                        <li>Go to the User tab</li>
                        <li>Find the API Keys section</li>
                        <li>Enter your keys for each service you want to use</li>
                    </ol>
                    <p>This provides better security and consistent key management across SwarmUI.</p>
                `;
                apiKeySection.prepend(infoDiv);
            }
            
            // Disable all API key radio buttons
            const apiKeyRadios = apiKeySection.querySelectorAll('input[name="apiKeyBackend"]');
            apiKeyRadios.forEach(radio => {
                radio.disabled = true;
            });
            
            // Disable the save button
            const saveKeyButton = apiKeySection.querySelector('button[onclick="saveApiKey()"]');
            if (saveKeyButton) {
                saveKeyButton.textContent = 'API Keys Moved to User Tab';
                saveKeyButton.classList.add('disabled');
            }
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

        // Remove API key backend selection event listeners - API keys are now managed in User tab
        const apiKeyRadios = document.querySelectorAll('input[name="apiKeyBackend"]');
        apiKeyRadios.forEach(radio => {
            radio.removeEventListener('change', updateApiKeyInput);
        });
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
    console.log("API keys are now managed in the User tab");
}

/**
 * Saves API key for the selected backend
 */
function saveApiKey() {
    // Display message to direct users to the User tab for API key management
    showMessage('info', 'API keys are now managed in the User tab under API Keys section.');
    showError('API keys are now managed in the User tab instead of here. Please go to the User tab to set your API keys.')
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

/**
 * Opens the User tab and navigates to the API Keys section
 * Used when a user needs to set API keys after receiving an error
 */
function openUserApiKeysSettings() {
    try {
        // Close settings modal if open
        closeSettingsModal();
        
        // Send a message to the parent window to open the User tab
        window.parent.postMessage({
            type: 'navigate',
            target: 'user',
            section: 'api-keys'
        }, '*');
        
        showMessage('info', 'Navigating to User tab API Keys section...');
    } catch (error) {
        console.error('Error navigating to User API Keys section:', error);
        showMessage('error', 'Could not navigate to User tab automatically. Please go to the User tab and select API Keys section manually.');
    }
}

/**
 * Handles API key errors by showing a helpful message and providing options
 * @param {string} service - The service name (e.g., 'OpenAI', 'Anthropic')
 * @param {string} error - The error message
 */
function handleApiKeyError(service, error) {
    const errorEl = document.createElement('div');
    errorEl.className = 'api-key-error';
    errorEl.innerHTML = `
        <p><strong>${service} API Key Error:</strong> ${error}</p>
        <p>API keys are now managed in the User tab under API Keys section.</p>
        <button class="btn btn-primary" onclick="openUserApiKeysSettings()">Go to API Keys Settings</button>
    `;
    
    // Show the error in the UI
    const chatContainer = document.querySelector('.chat-container');
    if (chatContainer) {
        chatContainer.appendChild(errorEl);
        chatContainer.scrollTop = chatContainer.scrollHeight;
    }
    
    // Also log to console
    console.error(`${service} API Key Error:`, error);
}

/**
 * Shows an error message that also includes API key help if the error contains API key related keywords
 * @param {string} message - The error message
 */
function showChatError(message) {
    showError(message);
    
    // Check if error is API key related
    const apiKeyErrors = {
        'OpenAI API Key not found': 'openai',
        'Anthropic API Key not found': 'anthropic',
        'OpenRouter API Key not found': 'openrouter'
    };
    
    for (const [errorText, service] of Object.entries(apiKeyErrors)) {
        if (message.includes(errorText)) {
            handleApiKeyError(service.charAt(0).toUpperCase() + service.slice(1), message);
            return;
        }
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