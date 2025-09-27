/**
 * magicprompt.js
 * Core functionality and utilities for the MagicPrompt extension.
 * 
 * This file has been updated to move settings-related functions to settings.js
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
                console.log('Making API request with instructions:', payload.messageContent.instructions);
                try {
                    return new Promise((resolve, reject) => {
                        genericRequest('MagicPromptPhoneHome', payload,
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
                // only allow input to be empty for 'random-prompt' action
                if (!input?.trim() && action !== 'random-prompt') {
                    throw new Error('Input is required');
                }
                const hasImage = Boolean(image);
                const featureName = action.toLowerCase();
                console.log(`Action: "${action}", featureName: "${featureName}"`);

                // Use the feature mapping system to get the right instruction type
                const instructionType = getInstructionForFeature(featureName);
                console.log(`Instruction type from feature mapping: "${instructionType}"`);

                // If no mapping found, use the featureName as fallback
                const effectiveType = instructionType || featureName;
                console.log(`Effective instruction type: "${effectiveType}"`);

                const instructions = getInstructionContent(effectiveType);
                try {
                    // Get model and backend based on request type
                    const modelId = this.getModelId(hasImage);
                    const backend = hasImage ? MP.settings.visionbackend : MP.settings.backend;
                    // Get appropriate instructions based on action type and feature mapping
                    const featureName = action.toLowerCase();
                    // Use the feature mapping system to get the right instruction type
                    const instructionType = getInstructionForFeature(featureName) || featureName;
                    const instructions = getInstructionContent(instructionType);
                    console.log('CreaterequestPayload: Using instructions:', instructions);
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
                        action: featureName,
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

/**
 * Handles the enhance prompt button click
 * Takes the current prompt text and enhances it using the LLM
 */

async function handleEnhancePrompt() {
  
  const promptTextArea = document.getElementById("alt_prompt_textbox");

  if (window.isEnhancing) return;
  const loadingAnimation = document.getElementById("prompt_loading_animation");
  try {
    window.isEnhancing = true;
    // Show loading animation
    if (loadingAnimation) loadingAnimation.classList.add("active");
    let input = promptTextArea.value.trim();
     if(!input) {
        // Create a random prompt if the input is empty
        const inputPayload = MP.RequestBuilder.createRequestPayload(
            'Generate a creative and interesting random prompt for image generation.',
            null,
            'random-prompt'
        );
        const response = await MP.APIClient.makeRequest(inputPayload);
        if (response.success && response.response) {
            input = response.response;
        } else {
            throw new Error(response.error || 'Failed to generate random prompt');
        }
    }
    const payload = MP.RequestBuilder.createRequestPayload(
      input,
      null,
      'enhance-prompt'
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
    // Hide loading animation
    if (loadingAnimation) loadingAnimation.classList.remove('active');
  }
}

/**
 * Handles the magic vision button click
 * Analyzes the current image and generates a prompt based on it
 * @returns {void}
 * @throws {Error} If no image is selected or if the analysis fails
 * */
async function handleVisionAnalysis() {
    const currentImage = document.querySelector('#current_image img.current-image-img');
    if (!currentImage?.src) {
        showError('No image selected');
        return;
    }
    const loadingAnimation = document.getElementById('prompt_loading_animation');
    try {
        // Show loading animation
        if (loadingAnimation) loadingAnimation.classList.add('active');
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
        // Get instruction for magic-vision feature
        const payload = MP.RequestBuilder.createRequestPayload(
            getInstructionContent(getInstructionForFeature('magic-vision') || 'caption'),
            base64Data,
            'magic-vision'
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
    } finally {
        // Hide loading animation
        if (loadingAnimation) loadingAnimation.classList.remove('active');
    }
}

/**
 * Adds the prompt buttons to the Generate tab with a mini-settings panel
 * @returns {void}
 */
function addPromptButtons() {
    const altPromptRegion = document.querySelector('.alt_prompt_region');
    if (!altPromptRegion) return;
    // Create container
    const container = document.createElement('div');
    container.className = 'magicprompt prompt-buttons-container';
    // Create loading animation
    const loadingAnimation = document.createElement('div');
    loadingAnimation.className = 'magicprompt prompt-loading';
    loadingAnimation.id = 'prompt_loading_animation';
    for (let i = 0; i < 3; i++) {
        const dot = document.createElement('div');
        dot.className = 'dot';
        loadingAnimation.appendChild(dot);
    }
    // Create enhance button
    const enhanceButton = document.createElement('button');
    enhanceButton.className = 'magicprompt prompt-button';
    enhanceButton.innerHTML = '🪄 Enhance Prompt';
    enhanceButton.addEventListener('click', handleEnhancePrompt);
    // Create vision button
    const visionButton = document.createElement('button');
    visionButton.className = 'magicprompt prompt-button';
    visionButton.innerHTML = '👀 Magic Vision';
    visionButton.addEventListener('click', handleVisionAnalysis);
    // Create settings button
    const settingsButton = document.createElement('button');
    settingsButton.className = 'magicprompt prompt-settings-button';
    settingsButton.innerHTML = '⚙️';
    settingsButton.title = 'Feature Settings';
    // Create settings panel
    const settingsPanel = document.createElement('div');
    settingsPanel.className = 'magicprompt prompt-settings-panel';
    settingsPanel.style.display = 'none';
    document.body.appendChild(settingsPanel);
    // Build settings panel content
    settingsPanel.innerHTML = `
        <div class="settings-panel-header">
            <h3>Feature Settings</h3>
            <button class="panel-close-btn">×</button>
        </div>
        <div class="settings-panel-body">
            <div class="feature-setting">
                <label for="enhance-prompt-select">Enhance Prompt Instruction:</label>
                <select id="enhance-prompt-select" class="feature-select"></select>
                <div class="setting-description">Choose which instruction set to use when enhancing prompts</div>
            </div>
            <div class="feature-setting">
                <label for="magic-vision-select">Magic Vision Instruction:</label>
                <select id="magic-vision-select" class="feature-select"></select>
                <div class="setting-description">Choose which instruction set to use for image analysis</div>
            </div>
        </div>
    `;

    // Function to position the panel
    function positionSettingsPanel() {
        const buttonRect = settingsButton.getBoundingClientRect();
        settingsPanel.style.position = 'fixed';
        settingsPanel.style.top = (buttonRect.bottom + 10) + 'px';
        settingsPanel.style.right = (window.innerWidth - buttonRect.right) + 'px';
        settingsPanel.style.zIndex = '10000';
    }

    // Function to populate the feature selects
    function populateFeatureSelects() {
        const enhanceSelect = settingsPanel.querySelector('#enhance-prompt-select');
        const visionSelect = settingsPanel.querySelector('#magic-vision-select');
        if (enhanceSelect && typeof getInstructionsForCategory === 'function') {
            enhanceSelect.innerHTML = '';
            const enhanceInstructions = getInstructionsForCategory('prompt');
            enhanceInstructions.forEach(instruction => {
                const option = document.createElement('option');
                option.value = instruction.id;
                option.textContent = instruction.title;
                enhanceSelect.appendChild(option);
            });
            const currentEnhanceMapping = getInstructionForFeature('enhance-prompt');
            if (currentEnhanceMapping && enhanceSelect.querySelector(`option[value="${currentEnhanceMapping}"]`)) {
                enhanceSelect.value = currentEnhanceMapping;
            }
        }
        if (visionSelect && typeof getInstructionsForCategory === 'function') {
            visionSelect.innerHTML = '';
            const visionInstructions = getInstructionsForCategory('vision');
            visionInstructions.forEach(instruction => {
                const option = document.createElement('option');
                option.value = instruction.id;
                option.textContent = instruction.title;
                visionSelect.appendChild(option);
            });
            const currentVisionMapping = getInstructionForFeature('magic-vision');
            if (currentVisionMapping && visionSelect.querySelector(`option[value="${currentVisionMapping}"]`)) {
                visionSelect.value = currentVisionMapping;
            }
        }
    }

    // Add change handlers to the selects
    function addSelectHandlers() {
        const enhanceSelect = settingsPanel.querySelector('#enhance-prompt-select');
        const visionSelect = settingsPanel.querySelector('#magic-vision-select');
        if (enhanceSelect && typeof setInstructionForFeature === 'function') {
            enhanceSelect.addEventListener('change', function () {
                setInstructionForFeature('enhance-prompt', this.value);
            });
        }
        if (visionSelect && typeof setInstructionForFeature === 'function') {
            visionSelect.addEventListener('change', function () {
                setInstructionForFeature('magic-vision', this.value);
            });
        }
    }
    // Toggle settings panel when clicking settings button
    settingsButton.addEventListener('click', function (e) {
        e.stopPropagation();
        const isVisible = settingsPanel.style.display === 'block';
        if (isVisible) {
            settingsPanel.style.display = 'none';
            return;
        }
        populateFeatureSelects();
        addSelectHandlers();
        positionSettingsPanel();
        settingsPanel.style.display = 'block';
    });
    // Close panel when clicking the close button
    const closeBtn = settingsPanel.querySelector('.panel-close-btn');
    if (closeBtn) {
        closeBtn.addEventListener('click', function () {
            settingsPanel.style.display = 'none';
        });
    }
    // Close panel when clicking outside
    document.addEventListener('click', function (e) {
        if (settingsPanel.style.display === 'block' &&
            !settingsPanel.contains(e.target) &&
            !settingsButton.contains(e.target)) {
            settingsPanel.style.display = 'none';
        }
    });
    // Add elements to container
    container.appendChild(loadingAnimation);
    container.appendChild(enhanceButton);
    container.appendChild(visionButton);
    container.appendChild(settingsButton);
    // Insert container into the alt prompt region
    altPromptRegion.insertBefore(container, altPromptRegion.firstChild);
}

function wildcardSeedGenerator() {
    document.addEventListener('click', function (e) {
        const MAX_WC_SEED = 4294967295;
        const extensionEnabled = document.getElementById('input_group_content_magicprompt_toggle');
        const generateEnabled = document.getElementById('input_mpgeneratewildcardseed');

        if (
            !e.target
            || e.target.id !== 'alt_generate_button'
            || !extensionEnabled
            || !extensionEnabled.checked
            || !generateEnabled
            || !generateEnabled.checked
        ) {
            return;
        }

        let wildcardSeedElem = getRequiredElementById('input_wildcardseed');
        wildcardSeedElem.value = Math.floor(Math.random() * MAX_WC_SEED);
        triggerChangeFor(wildcardSeedElem);
    }, true);
}

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
        // Setup auto wildcard seed generation on Generate click (capture phase, before onclick)
        wildcardSeedGenerator();
        // Initialize modal
        $('#settingsModal').modal({
            backdrop: 'static', keyboard: false, show: false
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

promptTabComplete.registerPrefix('mporiginal', 'Placeholder for the original prompt', (prefix) => {
    return [];
}, true);
