/**
 * settings.js
 * Handles all settings functionality for the MagicPrompt extension.
 * Manages both UI and backend operations for configuring the extension.
 */

'use strict';

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
                    const error = new Error(data.error || 'Failed to load settings');
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
        const chatBaseUrl = document.getElementById('backendUrl')?.value ||
            MP.settings.backends[chatBackendId]?.baseurl;
        const visionBaseUrl = isLinked ? chatBaseUrl :
            (document.getElementById('visionBackendUrl')?.value ||
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
            instructions: MP.settings.instructions
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
        initInstructionsUI();
        closeSettingsModal();
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
 * Initialize instructions UI with radio buttons and a single textarea
 * Manages switching between different instruction types
 */
function initInstructionsUI() {
    // Get references to elements
    const instructionTypeRadios = document.querySelectorAll('input[name="instructionType"]');
    const instructionTextarea = document.getElementById('instructionTextarea');
    const instructionLabel = document.getElementById('instructionLabel');
    const instructionHelpText = document.getElementById('instructionHelpText');

    if (!instructionTextarea || !instructionLabel || !instructionHelpText) {
        console.error('Instruction UI elements not found');
        return;
    }

    // Define content for each instruction type
    const instructionTypes = {
        chat: {
            title: 'Chat Instructions',
            tooltip: 'Instructions for how the AI should behave in chat conversations. These define the AI\'s personality and response style.',
            helpText: 'Define how the AI should behave in chat conversations',
            placeholder: 'Configure the AI\'s chat personality and behavior'
        },
        vision: {
            title: 'Vision Instructions',
            tooltip: 'Instructions for how the AI should analyze and describe images in the vision tab. These instructions are used when discussing images.',
            helpText: 'Guide how the AI analyzes and describes images',
            placeholder: 'Configure how the AI should analyze and describe images'
        },
        caption: {
            title: 'Image Caption Instructions',
            tooltip: 'Instructions for how the AI should generate image captions. These instructions are used when clicking the \'Caption\' button in the vision tab.',
            helpText: 'Define how the AI generates captions for images',
            placeholder: 'Configure how the AI should generate image captions'
        },
        prompt: {
            title: 'Enhance Prompt Instructions',
            tooltip: 'Instructions for how the AI should format text-to-image prompts when generating images. These instructions are used when clicking \'Enhance Prompt\' in the generate tab.',
            helpText: 'These instructions guide how the AI formats and enhances prompts',
            placeholder: 'How do you want the AI to Enhance your prompt?'
        }
    };

    // Add custom instructions if they exist
    if (MP.settings.instructions?.custom) {
        Object.entries(MP.settings.instructions.custom).forEach(([key, value]) => {
            if (typeof value === 'object' && value.title) {
                instructionTypes[key] = value;
            }
        });
    }

    // Function to update the instruction UI based on selected type
    function updateInstructionUI(type) {
        console.log(`Updating instruction UI for type: ${type}`);

        // Get the type config
        const typeConfig = instructionTypes[type];
        if (!typeConfig) {
            console.error(`Instruction type "${type}" not found`);
            return;
        }

        console.log(`Type config:`, typeConfig);

        // Update the label, tooltip, and help text
        if (instructionLabel) {
            // Find or create text node for title
            let textNode = Array.from(instructionLabel.childNodes)
                .find(node => node.nodeType === Node.TEXT_NODE);

            if (!textNode) {
                textNode = document.createTextNode('');
                instructionLabel.insertBefore(textNode, instructionLabel.firstChild);
            }

            // Update the text
            textNode.nodeValue = typeConfig.title + ' ';

            // Update the tooltip
            const tooltipIcon = instructionLabel.querySelector('i');
            if (tooltipIcon) {
                tooltipIcon.setAttribute('title', typeConfig.tooltip);
                tooltipIcon.setAttribute('data-bs-original-title', typeConfig.tooltip);

                // Refresh the tooltip if Bootstrap's tooltip is initialized
                if (typeof bootstrap !== 'undefined' && bootstrap.Tooltip) {
                    const tooltip = bootstrap.Tooltip.getInstance(tooltipIcon);
                    if (tooltip) {
                        tooltip.dispose();
                    }
                    new bootstrap.Tooltip(tooltipIcon);
                }
            }
        }

        // Update help text
        if (instructionHelpText) {
            instructionHelpText.textContent = typeConfig.helpText;
        }

        // Update placeholder
        if (instructionTextarea) {
            instructionTextarea.placeholder = typeConfig.placeholder;

            // Get the current instructions for this type
            let value = '';

            if (type === 'chat' || type === 'vision' || type === 'caption' || type === 'prompt') {
                console.log(`Getting ${type} instructions:`, MP.settings.instructions[type]);
                value = MP.settings.instructions[type] || '';
            } else if (MP.settings.instructions?.custom?.[type]) {
                // This is a custom instruction
                value = typeof MP.settings.instructions.custom[type] === 'object'
                    ? MP.settings.instructions.custom[type].content || ''
                    : MP.settings.instructions.custom[type] || '';
            }

            console.log(`Setting textarea value to:`, value);

            // Set the textarea value and force a refresh
            instructionTextarea.value = value;

            // Force a refresh of the textarea
            setTimeout(() => {
                const event = new Event('input', { bubbles: true });
                instructionTextarea.dispatchEvent(event);
            }, 0);
        }
    }

    // Add event listeners to radio buttons
    instructionTypeRadios.forEach(radio => {
        radio.addEventListener('change', (e) => {
            if (e.target.checked) {
                console.log(`Radio button changed: ${e.target.id}`);

                // Only save content if the textarea exists
                if (instructionTextarea) {
                    const currentType = Array.from(instructionTypeRadios)
                        .find(r => r.checked && r !== e.target)?.id.replace('InstructionBtn', '').toLowerCase();

                    if (currentType) {
                        console.log(`Saving content for ${currentType} before switching`);
                        saveCurrentInstructionContent(currentType);
                    }
                }

                // Extract type from button ID
                const type = e.target.id.replace('InstructionBtn', '').toLowerCase();
                console.log(`Switching to instruction type: ${type}`);

                // Update UI with the new type
                updateInstructionUI(type);
            }
        });
    });

    // Function to save the current instruction content
    function saveCurrentInstructionContent(specificType) {
        if (!instructionTextarea) return;

        const selectedType = specificType || Array.from(instructionTypeRadios)
            .find(r => r.checked)?.id.replace('InstructionBtn', '').toLowerCase();

        if (!selectedType) return;

        console.log(`Saving content for ${selectedType}:`, instructionTextarea.value);

        if (selectedType === 'chat' || selectedType === 'vision' ||
            selectedType === 'caption' || selectedType === 'prompt') {
            // Only save if the content has actually changed
            if (MP.settings.instructions[selectedType] !== instructionTextarea.value) {
                MP.settings.instructions[selectedType] = instructionTextarea.value;
                console.log(`Saved to MP.settings.instructions.${selectedType}`);
            } else {
                console.log(`Content for ${selectedType} unchanged, not saving`);
            }
        } else if (MP.settings.instructions?.custom) {
            // This is a custom instruction
            if (!MP.settings.instructions.custom[selectedType]) {
                MP.settings.instructions.custom[selectedType] = {};
            }

            const currentContent = typeof MP.settings.instructions.custom[selectedType] === 'object'
                ? MP.settings.instructions.custom[selectedType].content || ''
                : MP.settings.instructions.custom[selectedType] || '';

            // Only save if changed
            if (currentContent !== instructionTextarea.value) {
                if (typeof MP.settings.instructions.custom[selectedType] === 'object') {
                    MP.settings.instructions.custom[selectedType].content = instructionTextarea.value;
                } else {
                    MP.settings.instructions.custom[selectedType] = instructionTextarea.value;
                }
                console.log(`Saved to MP.settings.instructions.custom.${selectedType}`);
            } else {
                console.log(`Custom content for ${selectedType} unchanged, not saving`);
            }
        }
    }

    // Save the current instruction when it changes
    if (instructionTextarea) {
        // Use a debounced approach to avoid excessive saves
        let saveTimeout = null;
        instructionTextarea.addEventListener('input', () => {
            // Clear any pending save
            if (saveTimeout) {
                clearTimeout(saveTimeout);
            }

            // Set a new timeout to save after typing stops
            saveTimeout = setTimeout(() => {
                const selectedType = Array.from(instructionTypeRadios)
                    .find(r => r.checked)?.id.replace('InstructionBtn', '').toLowerCase();

                if (selectedType) {
                    console.log(`Saving ${selectedType} after input change`);
                    saveCurrentInstructionContent(selectedType);
                }
                saveTimeout = null;
            }, 500); // Wait 500ms after typing stops
        });
    }

    // Ensure instructions are properly initialized
    if (!MP.settings.instructions) {
        console.log("Initializing empty instructions object");
        MP.settings.instructions = {
            chat: '',
            vision: '',
            caption: '',
            prompt: ''
        };
    } else {
        console.log("Current instructions:", MP.settings.instructions);
    }

    // Initialize with the selected instruction type (default to chat)
    const selectedType = Array.from(instructionTypeRadios)
        .find(r => r.checked)?.id.replace('InstructionBtn', '').toLowerCase() || 'chat';

    console.log(`Initial selected instruction type: ${selectedType}`);

    // Make sure the correct radio button is checked
    const selectedRadio = document.getElementById(`${selectedType}InstructionBtn`);
    if (selectedRadio) {
        selectedRadio.checked = true;
        console.log(`Set radio button ${selectedRadio.id} to checked`);
    }

    // Update UI with the selected type's content
    console.log(`Initializing UI with ${selectedType} instructions`);
    updateInstructionUI(selectedType);

    return {
        updateInstructionUI,
        saveCurrentInstructionContent,
        addCustomInstructionType: function (key, config) {
            // Add a new instruction type
            instructionTypes[key] = config;

            // Create and add a new radio button if it doesn't exist
            if (!document.getElementById(`${key}InstructionBtn`)) {
                const instructionTypeGroup = document.getElementById('instructionTypeGroup');
                if (instructionTypeGroup) {
                    // Create the radio input
                    const radio = document.createElement('input');
                    radio.type = 'radio';
                    radio.className = 'btn-check';
                    radio.name = 'instructionType';
                    radio.id = `${key}InstructionBtn`;

                    // Create the label
                    const label = document.createElement('label');
                    label.className = 'btn btn-outline-primary';
                    label.htmlFor = `${key}InstructionBtn`;
                    label.textContent = config.title || key;

                    // Add to the group
                    instructionTypeGroup.appendChild(radio);
                    instructionTypeGroup.appendChild(label);

                    // Add event listener
                    radio.addEventListener('change', (e) => {
                        saveCurrentInstructionContent();
                        updateInstructionUI(key);
                    });
                }
            }
        }
    };
}

/**
 * Initializes settings modal with saved values
 */
function initSettingsModal() {
    try {
        console.log("Initializing settings modal with current settings:", MP.settings);

        // Ensure the instructions object has the correct structure
        if (!MP.settings.instructions) {
            MP.settings.instructions = {
                chat: '',
                vision: '',
                caption: '',
                prompt: ''
            };
        }

        // Log all instruction types to debug
        console.log("Current instructions by type:");
        console.log("Chat:", MP.settings.instructions.chat);
        console.log("Vision:", MP.settings.instructions.vision);
        console.log("Caption:", MP.settings.instructions.caption);
        console.log("Prompt:", MP.settings.instructions.prompt);

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
        const backendUrl = document.getElementById('backendUrl');
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
        const visionBackendUrl = document.getElementById('visionBackendUrl');
        if (visionBackendUrl) {
            visionBackendUrl.value = MP.settings.backends[currentVisionBackend]?.baseurl || '';
        }

        // Initialize the instructions UI
        initInstructionsUI();

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
                    const backendUrl = document.getElementById('backendUrl');
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
                    const backendUrl = document.getElementById('visionBackendUrl');
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
        const backendBaseUrl = document.getElementById('backendUrl');
        if (backendBaseUrl) {
            backendBaseUrl.addEventListener('input', function (e) {
                // Sync with vision URL when linked
                if (document.getElementById('linkModelsToggle')?.checked) {
                    const visionBackendUrl = document.getElementById('visionBackendUrl');
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
        const visionBackendUrlInput = document.getElementById('visionBackendUrl');
        if (visionBackendUrlInput) {
            visionBackendUrlInput.addEventListener('input', function (e) {
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
            toggleBtn.addEventListener('change', function (e) {
                const isLinked = e.target.checked;
                updateLinkedModelsUI(isLinked);
            });
        }

        // Add event listener for model select change
        const modelDropdown = document.getElementById("modelSelect");
        if (modelDropdown) {
            modelDropdown.addEventListener('change', function (e) {
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
            radio.addEventListener('change', function (e) {
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

/**
 * Updates UI for linked/unlinked chat and vision models
 * @param {boolean} isLinked - Whether models are linked
 */
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
            const backendUrl = document.getElementById('backendUrl');
            const visionBackendUrl = document.getElementById('visionBackendUrl');
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

// Expose functions to global scope
if (typeof window !== 'undefined') {
    window.initSettingsModal = initSettingsModal;
    window.closeSettingsModal = closeSettingsModal;
    window.saveSettings = saveSettings;
    window.resetSettings = resetSettings;
    window.loadSettings = loadSettings;
    window.updateLinkedModelsUI = updateLinkedModelsUI;
}

// Add to MP namespace
if (typeof MP !== 'undefined') {
    MP.Settings = {
        initSettingsModal,
        closeSettingsModal,
        saveSettings,
        resetSettings,
        loadSettings,
        updateLinkedModelsUI,
        fetchModels,
        initInstructionsUI
    };
}