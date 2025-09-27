/**
 * settings.js
 * Handles all settings functionality for the MagicPrompt extension.
 * Manages both UI and backend operations for configuring the extension.
 */

'use strict';

// Define backends that don't need base URL configuration
const FIXED_URL_BACKENDS = ['openai', 'anthropic', 'openrouter', 'grok'];

// Define default feature to instruction mappings
const DEFAULT_FEATURE_MAPPINGS = {
  'enhance-prompt': 'prompt',
  'magic-vision': 'caption',
  'chat-mode': 'chat',
  'vision-mode': 'vision',
  'prompt-mode': 'prompt',
  'random-prompt': 'randomprompt',
   caption: 'caption',
  'generate-instruction': 'instructiongen',
};

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
 * Creates a debounced function that delays invoking func until after wait milliseconds
 * @param {Function} func - Function to debounce
 * @param {number} wait - Milliseconds to wait
 * @returns {Function} Debounced function
 */
function debounce(func, wait) {
  let timeout;
  return function executedFunction(...args) {
    const later = () => {
      clearTimeout(timeout);
      func(...args);
    };
    clearTimeout(timeout);
    timeout = setTimeout(later, wait);
  };
}

/**
 * Loads settings from the backend
 * @returns {Promise<Object>} Settings object
 * @throws {Error} If settings cannot be loaded
 */
async function loadSettings() {
  try {
    const response = await new Promise((resolve, reject) => {
        genericRequest('GetMagicPromptSettings', {}, (data) => {
        if (data.success) {
          const serverSettings = data.settings;
          // Create settings object preserving server values
          const settings = {
            // Core settings
            backend: serverSettings.backend || 'ollama',
            model: serverSettings.model || '',
            visionbackend:
              serverSettings.visionbackend ||
              serverSettings.backend ||
              'ollama',
            visionmodel: serverSettings.visionmodel || '',
            linkChatAndVisionModels:
              serverSettings.linkChatAndVisionModels !== false, // Default to true if not set
            // Backends - merge using spread operator which does a "deep merge" of two objects
            backends: {
              ...MP.settings.backends, // Start with default endpoints
              ...(serverSettings.backends || {}), // Overlay server settings
            },
            // Instructions - ensure we get the proper values from the server
            instructions: {
              chat: '',
              vision: '',
              caption: '',
              prompt: '',
              randomprompt: '',
              custom: {},
              featureMap: { ...DEFAULT_FEATURE_MAPPINGS },
            },
          };
          // Handle the instructions specifically to ensure all types are properly loaded
          if (serverSettings.instructions) {
            // Explicitly handle each instruction type
            if (typeof serverSettings.instructions.chat === 'string') {
              settings.instructions.chat = serverSettings.instructions.chat;
            }
            
            if (typeof serverSettings.instructions.vision === 'string') {
              settings.instructions.vision = serverSettings.instructions.vision;
            }

            if (typeof serverSettings.instructions.caption === 'string') {
              settings.instructions.caption =
                serverSettings.instructions.caption;
            }
            if (typeof serverSettings.instructions.prompt === 'string') {
              settings.instructions.prompt = serverSettings.instructions.prompt;
            }
            if (typeof serverSettings.instructions.randomprompt === 'string') {
              settings.instructions.randomprompt = serverSettings.instructions.randomprompt;
            }
            // Handle any custom instructions
            if (serverSettings.instructions.custom) {
              settings.instructions.custom = serverSettings.instructions.custom;
            }
            // Handle feature mappings
            if (serverSettings.instructions.featureMap) {
              settings.instructions.featureMap = {
                ...DEFAULT_FEATURE_MAPPINGS,
                ...serverSettings.instructions.featureMap,
              };
            }
          }
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
 * @param {boolean} [skipFeatureMappings=false] - If true, don't reload feature mappings from UI
 * @returns {Promise<void>}
 */
async function saveSettings(skipFeatureMappings = false) {
  try {
    // Check if models are linked
    const isLinked = document.getElementById('linkModelsToggle')?.checked;
    // Get the selected backends
    const selectedChatBackend = document.querySelector(
      'input[name="llmBackend"]:checked'
    );
    const selectedVisionBackend = document.querySelector(
      'input[name="visionBackendSelect"]:checked'
    );
    // Extract backend IDs
    const chatBackendId = selectedChatBackend
      ? selectedChatBackend.id.replace('LLMBtn', '').toLowerCase()
      : MP.settings.backend;
    // If linked, use chat backend for vision too
    const visionBackendId = isLinked
      ? chatBackendId
      : selectedVisionBackend
      ? selectedVisionBackend.id.replace('VisionBtn', '').toLowerCase()
      : MP.settings.visionbackend;
    // Get models and use chat model for vision if linked
    const chatModel =
      document.getElementById('modelSelect')?.value || MP.settings.model;
    const visionModel = isLinked
      ? chatModel
      : document.getElementById('visionModel')?.value ||
        MP.settings.visionmodel;
    // Get base URLs
    const chatBaseUrl =
      document.getElementById('backendUrl')?.value ||
      MP.settings.backends[chatBackendId]?.baseurl;
    const visionBaseUrl = isLinked
      ? chatBaseUrl
      : document.getElementById('visionBackendUrl')?.value ||
        MP.settings.backends[visionBackendId]?.baseurl;
    // Save feature mappings if not skipped
    if (!skipFeatureMappings) {
      saveFeatureMappingsFromUI();
    }
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
        },
      },
      instructions: MP.settings.instructions,
    };
    // Update MP.settings with the new values
    MP.settings = settings;
    // Create request payload with settings properly wrapped
    const payload = {
      settings,
    };
    // Use genericRequest which will automatically add session_id
    genericRequest('SaveMagicPromptSettings', payload, (data) => {
        if (data.success) {
          console.log('Settings saved successfully');
          updateModelListOnLeft();
        } else {
          console.error(
            `Failed to save settings: ${data.error || 'Unknown error'}`
          );
          // showError(`Failed to save settings: ${data.error || 'Unknown error'}`);
        }
      },
      0, // depth
      (error) => {
        console.error('Error saving settings:', error);
        // showError(`Error saving settings: ${error}`);
      }
    );
  } catch (error) {
    console.error('Error in saveSettings:', error);
    // showError(`Error in saveSettings: ${error.message}`);
  }
}

/**
 * Resets settings to defaults
 * @returns {Promise<void>}
 */
async function resetSettings() {
  try {
    if (
      !confirm(
        'This will reset all settings to defaults. Any custom API keys and custom instructions will be cleared. Are you sure?'
      )
    ) {
      return;
    }
    const response = await new Promise((resolve, reject) => {
      genericRequest('ResetMagicPromptSettings', {}, (response) => {
        if (response.success) {
          resolve(response);
        } else {
          reject(new Error(response.error || 'Failed to reset settings'));
        }
      });
    });
    MP.settings = response.settings;
    // Ensure feature mappings are set to defaults
    if (!MP.settings.instructions.featureMap) {
      MP.settings.instructions.featureMap = { ...DEFAULT_FEATURE_MAPPINGS };
    }
    await fetchModels();
    // Reset UI elements
    initInstructionsUI();
    renderCustomInstructionsList();
    populateFeatureSelects();
    closeSettingsModal();
  } catch (error) {
    console.error('Settings reset error:', error);
    // showError(`Failed to reset settings: ${error.message}`);
  }
}

/**
 * Fetches available models from the backend
 * @returns {Promise<Object>} Object containing the fetched models
 */
async function fetchModels() {
  const modelSelect = document.getElementById('modelSelect');
  const visionModelSelect = document.getElementById('visionModel');

  if (!modelSelect || !visionModelSelect) {
    console.error('Model select elements not found');
    return Promise.reject(new Error('Model select elements not found'));
  }

  // Clear existing models when starting a new fetch
  modelSelect.innerHTML = '';
  visionModelSelect.innerHTML = '';

  // Add loading options
  const loadingOption = new Option('Loading models...', '');
  loadingOption.disabled = true;
  modelSelect.add(loadingOption.cloneNode(true));
  visionModelSelect.add(loadingOption.cloneNode(true));

  // Create a request semaphore
  if (MP.fetchingModels) {
    return MP.fetchingModelsPromise;
  }

  try {
    MP.fetchingModels = true;
    MP.fetchingModelsPromise = (async () => {
      // Use the currently set backend values from MP.settings
      const chatBackendId = MP.settings.backend || 'ollama';
      const visionBackendId = MP.settings.visionbackend || 'ollama';

      // Fetch models for both backends
      const response = await new Promise((resolve, reject) => {
        genericRequest(
          'GetMagicPromptModels',
          {
            backend: chatBackendId,
            visionbackend: visionBackendId,
            backends: {
              [chatBackendId]: {
                baseurl: MP.settings.backends[chatBackendId]?.baseurl || '',
              },
              [visionBackendId]: {
                baseurl: MP.settings.backends[visionBackendId]?.baseurl || '',
              },
            },
          },
          (data) => {
            if (data.success) {
              resolve(data);
            } else {
              reject(new Error(data.error || 'Failed to fetch models'));
            }
          },
          0,
          (err) => {
            console.error('Error fetching models:', err);
            reject(new Error(err?.message || 'Failed to fetch models'));
          }
        );
      });

      // Clear existing models
      modelSelect.innerHTML = '';
      visionModelSelect.innerHTML = '';

      // Add chat models
      if (Array.isArray(response.models)) {
        const existingModelIds = new Set();
        const defaultOption = new Option('-- Select a model --', '');
        modelSelect.add(defaultOption);

        response.models.forEach((model) => {
          if (!model.model || existingModelIds.has(model.model)) {
            if (!model.model) {
              console.warn('Chat model missing model field:', model);
            }
            return;
          }
          existingModelIds.add(model.model);
          const option = new Option(model.name || model.model, model.model);
          modelSelect.add(option);
        });

        if (MP.settings.model) {
          setModelIfExists(modelSelect, MP.settings.model);
        }
      }

      // Add vision models
      if (Array.isArray(response.visionmodels)) {
        const existingVisionModelIds = new Set();
        const defaultOption = new Option('-- Select a vision model --', '');
        visionModelSelect.add(defaultOption);

        response.visionmodels.forEach((model) => {
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

      return response;
    })();

    return await MP.fetchingModelsPromise;
  } catch (error) {
    console.error('Error fetching models:', error);

    // Clear and show error state in dropdowns
    modelSelect.innerHTML = '';
    visionModelSelect.innerHTML = '';

    const errorOption = new Option(
      'Error loading models - Check URL and try again',
      ''
    );
    errorOption.disabled = true;
    modelSelect.add(errorOption.cloneNode(true));
    visionModelSelect.add(errorOption.cloneNode(true));

    return Promise.reject(error);
  } finally {
    // Clear semaphore
    MP.fetchingModels = false;
    MP.fetchingModelsPromise = null;
  }
}

function updateModelListOnLeft() {
  try {
    const modelSelect = document.getElementById('modelSelect');
    const enhanceInstructions = getInstructionsForCategory('prompt');
    const listOfModels = document.getElementById('input_mpmodelid');
    const listOfInstructions = document.getElementById('input_mpinstructions');

    if (!modelSelect || !listOfModels || !listOfInstructions) {
      console.warn('Could not sync MP List of Models: source or destination select not found');
      return;
    }

    listOfModels.innerHTML = '';
    Array.from(modelSelect.options).forEach(opt => {
      const option = new Option(opt.text, opt.value);
      listOfModels.add(option);
    });

    listOfInstructions.innerHTML = '';
    enhanceInstructions.forEach(instruction => {
      const option = new Option(instruction.title, instruction.id);
      listOfInstructions.add(option);
    });

    // Mirror current selection
    listOfModels.value = modelSelect.value || '';
    triggerChangeFor(listOfModels);
    triggerChangeFor(listOfInstructions);
  } catch (error) {
    console.error(error.message);
  }
}

/**
 * Sets model selection if it exists in options
 * @private
 * @param {HTMLSelectElement} select - Select element
 * @param {string} modelId - Model ID to select
 */
function setModelIfExists(select, modelId) {
  const modelExists = Array.from(select.options).some(
    (opt) => opt.value === modelId
  );
  if (modelExists) {
    select.value = modelId;
  }
}

/**
 * Gets the instruction content for a specified instruction type
 * @param {string} type - The instruction type (e.g., 'chat', 'vision', or a custom ID)
 * @returns {string} The instruction content
 */
function getInstructionContent(type) {
  if (!type) {
    return '';
  }
  // Handle built-in instruction types
   if (
    type === 'chat' ||
    type === 'vision' ||
    type === 'caption' ||
    type === 'prompt' ||
    type === 'randomprompt'
  ) {
    return MP.settings.instructions[type] || '';
  }
  // Handle custom instruction types
  if (MP.settings.instructions?.custom?.[type]) {
    const customInstruction = MP.settings.instructions.custom[type];
    if (customInstruction?.deleted === true) return '';
    return typeof customInstruction === 'object'
      ? customInstruction.content || ''
      : customInstruction || '';
  }
  return '';
}

/**
 * Gets the instruction type to use for a specific feature
 * @param {string} feature - The feature name (e.g., 'enhance-prompt', 'chat-mode')
 * @returns {string} The instruction type to use
 */
function getInstructionForFeature(feature) {
  if (!feature || !MP.settings.instructions?.featureMap) {
    return null;
  }
  const instructionType = MP.settings.instructions.featureMap[feature];
  return instructionType || null;
}

/**
 * Sets which instruction to use for a specific feature
 * @param {string} feature - The feature name
 * @param {string} instructionType - The instruction type to use
 * @param {boolean} [skipSave=false] - If true, don't call saveSettings() to prevent infinite loops
 */
function setInstructionForFeature(feature, instructionType, skipSave = false) {
  if (!feature) return;

  if (!MP.settings.instructions.featureMap) {
    MP.settings.instructions.featureMap = { ...DEFAULT_FEATURE_MAPPINGS };
  }
  MP.settings.instructions.featureMap[feature] = instructionType;
  if (!skipSave) {
    saveSettings();
  }
}

/**
 * Gets all instructions available for a specific category
 * @param {string} category - The category ('chat', 'vision', 'caption', 'prompt')
 * @returns {Array} Array of instruction objects with id and title
 */
function getInstructionsForCategory(category) {
  if (!category) return [];
  const instructions = [];
  // Add the built-in instruction for this category
  if (
    category === 'chat' ||
    category === 'vision' ||
    category === 'caption' ||
    category === 'prompt'
  ) {
    instructions.push({
      id: category,
      title: getCategoryTitle(category),
      isBuiltIn: true,
    });
  }
  // Add custom instructions that work with this category
  if (MP.settings.instructions?.custom) {
    Object.entries(MP.settings.instructions.custom).forEach(
      ([id, instruction]) => {
        if (instruction?.deleted === true) return;
        // Handle both string and object formats
        if (typeof instruction === 'object') {
          if (
            instruction.categories &&
            instruction.categories.includes(category)
          ) {
            instructions.push({
              id,
              title: instruction.title || id,
              isBuiltIn: false,
            });
          }
        }
      }
    );
  }
  return instructions;
}

/**
 * Gets a human-readable title for a category
 * @param {string} category - Category name
 * @returns {string} Human-readable title
 */
function getCategoryTitle(category) {
  switch (category) {
    case 'chat':
      return 'Chat (Default)';
    case 'vision':
      return 'Vision (Default)';
    case 'caption':
      return 'Caption (Default)';
    case 'prompt':
      return 'Enhance Prompt (Default)';
    default:
      return category.charAt(0).toUpperCase() + category.slice(1);
  }
}

/**
 * Populates the feature select dropdowns with available instructions
 */
function populateFeatureSelects() {
  const features = [
    { id: 'enhance-prompt', category: 'prompt' },
    { id: 'magic-vision', category: 'caption' },
    { id: 'chat-mode', category: 'chat' },
    { id: 'vision-mode', category: 'vision' },
    { id: 'caption', category: 'caption' },
  ];
  features.forEach((feature) => {
    const select = document.getElementById(`feature-${feature.id}`);
    if (!select) return;
    // Clear existing options
    select.innerHTML = '';
    // Get available instructions for this category
    const instructions = getInstructionsForCategory(feature.category);
    // Add options to select
    instructions.forEach((instruction) => {
      const option = document.createElement('option');
      option.value = instruction.id;
      option.textContent = instruction.title;
      select.appendChild(option);
    });
    // Set current selection
    const currentMapping = getInstructionForFeature(feature.id);
    if (
      currentMapping &&
      select.querySelector(`option[value="${currentMapping}"]`)
    ) {
      select.value = currentMapping;
    } else {
      // Default to the category name if no mapping or mapping not found
      select.value = feature.category;
    }
  });
}

/**
 * Saves the feature mappings from the UI selects
 */
function saveFeatureMappingsFromUI() {
  const features = [
    'enhance-prompt',
    'magic-vision',
    'chat-mode',
    'vision-mode',
    'caption',
  ];
  features.forEach((feature) => {
    const select = document.getElementById(`feature-${feature}`);
    if (select && select.value) {
      setInstructionForFeature(feature, select.value, true); // Pass true to skipSave
    }
  });
  // Save settings after updating all feature mappings
  saveSettings(true); // Pass true to skipFeatureMappings to prevent recursion
}

/**
 * Adds a new custom instruction
 * @param {Object} data - Instruction data
 * @returns {string} The ID of the new instruction
 */
function addCustomInstruction(data) {
  const id = data.id || `custom-${Date.now()}`;
  const categories = Array.isArray(data.categories)
    ? data.categories
    : typeof data.categories === 'string'
    ? [data.categories]
    : [];
  // Create the instruction object and add to settings
  const instruction = {
    id,
    title: data.title || 'Custom Instruction',
    content: data.content || '',
    tooltip: data.tooltip || `Custom instructions for ${data.title}`,
    categories,
    created: data.created || new Date().toISOString(),
    updated: new Date().toISOString(),
  };
  if (!MP.settings.instructions.custom) {
    MP.settings.instructions.custom = {};
  }
  MP.settings.instructions.custom[id] = instruction;
  saveSettings();
  addCustomInstructionToUI(id, instruction);
  return id;
}

/**
 * Updates an existing custom instruction
 * @param {string} id - Instruction ID
 * @param {Object} data - Updated instruction data
 * @returns {boolean} Success status
 */
function updateCustomInstruction(id, data) {
  if (!MP.settings.instructions?.custom?.[id]) {
    console.error(`Custom instruction "${id}" not found`);
    return false;
  }
  const instruction = MP.settings.instructions.custom[id];
  if (data.title) instruction.title = data.title;
  if (data.content !== undefined) instruction.content = data.content;
  if (data.tooltip) instruction.tooltip = data.tooltip;
  if (data.categories) {
    instruction.categories = Array.isArray(data.categories)
      ? data.categories
      : typeof data.categories === 'string'
      ? [data.categories]
      : instruction.categories;
  }
  instruction.updated = new Date().toISOString();
  updateCustomInstructionInUI(id);
  populateFeatureSelects();
  saveSettings();
  return true;
}

/**
 * Deletes a custom instruction
 * @param {string} id - Instruction ID
 * @returns {boolean} Success status
 */
function deleteCustomInstruction(id) {
  if (!MP.settings.instructions?.custom?.[id]) {
    console.error(`Custom instruction "${id}" not found`);
    return false;
  }
  MP.settings.instructions.custom[id] = {
    deleted: true,
    deletedAt: new Date().toISOString(),
  };
  // Handle feature mappings as before
  if (MP.settings.instructions.featureMap) {
    Object.entries(MP.settings.instructions.featureMap).forEach(
      ([feature, instructionId]) => {
        if (instructionId === id) {
          const categoryForFeature = DEFAULT_FEATURE_MAPPINGS[feature];
          MP.settings.instructions.featureMap[feature] = categoryForFeature;
        }
      }
    );
  }
  // Update UI and save
  removeCustomInstructionFromUI(id);
  populateFeatureSelects();
  saveSettings();
  return true;
}

/**
 * Adds a custom instruction to the UI
 * @param {string} id - Instruction ID
 * @param {Object} instruction - Instruction data
 */
function addCustomInstructionToUI(id, instruction) {
  const instructionTypeGroup = document.getElementById('instructionTypeGroup');
  if (instructionTypeGroup) {
    if (document.getElementById(`${id}InstructionBtn`)) {
      return updateCustomInstructionInUI(id);
    }
    // Create radio button for custom instruction
    const radio = document.createElement('input');
    radio.type = 'radio';
    radio.className = 'btn-check';
    radio.name = 'instructionType';
    radio.id = `${id}InstructionBtn`;
    // Create label for radio button
    const label = document.createElement('label');
    label.className = 'btn btn-outline-primary instruction-type-btn';
    label.htmlFor = radio.id;
    label.textContent = instruction.title;
    instructionTypeGroup.appendChild(radio);
    instructionTypeGroup.appendChild(label);
    // Add to instruction types for UI updating
    instructionTypes[id] = {
      title: instruction.title,
      tooltip: instruction.tooltip,
      helpText: `Custom instructions for ${instruction.categories.join(', ')}`,
      placeholder: `Enter custom instructions for ${instruction.title}`,
    };
    radio.addEventListener('change', handleInstructionChange);
  }
  renderCustomInstructionsList();
}

/**
 * Updates an existing custom instruction in the UI
 * @param {string} id - Instruction ID
 */
function updateCustomInstructionInUI(id) {
  if (!MP.settings.instructions?.custom?.[id]) return;
  const instruction = MP.settings.instructions.custom[id];
  // Update radio button label
  const label = document.querySelector(`label[for="${id}InstructionBtn"]`);
  if (label) {
    label.textContent = instruction.title;
  }
  // Update instruction types object
  if (instructionTypes[id]) {
    instructionTypes[id].title = instruction.title;
    instructionTypes[id].tooltip = instruction.tooltip;
    instructionTypes[
      id
    ].helpText = `Custom instructions for ${instruction.categories.join(', ')}`;
    instructionTypes[
      id
    ].placeholder = `Enter custom instructions for ${instruction.title}`;
  }
  // Refresh custom instructions list
  renderCustomInstructionsList();
}

/**
 * Removes a custom instruction from the UI
 * @param {string} id - Instruction ID
 */
function removeCustomInstructionFromUI(id) {
  // Remove radio button
  const radio = document.getElementById(`${id}InstructionBtn`);
  if (radio) {
    radio.remove();
  }
  // Remove label
  const label = document.querySelector(`label[for="${id}InstructionBtn"]`);
  if (label) {
    label.remove();
  }
  // Remove from instruction types
  if (instructionTypes[id]) {
    delete instructionTypes[id];
  }
  // Refresh custom instructions list
  renderCustomInstructionsList();
}

/**
 * Renders the list of custom instructions in the management section
 */
function renderCustomInstructionsList() {
  const container = document.getElementById('customInstructionsList');
  const noInstructionsMsg = document.getElementById('noCustomInstructionsMsg');
  if (!container) return;
  // Clear existing items (except the "no instructions" message)
  Array.from(container.children).forEach((child) => {
    if (child.id !== 'noCustomInstructionsMsg') {
      child.remove();
    }
  });
  const customInstructions = MP.settings.instructions?.custom || {};
  const instructionCount = Object.keys(customInstructions).length;
  // Show/hide the "no instructions" message
  if (noInstructionsMsg) {
    noInstructionsMsg.style.display = instructionCount > 0 ? 'none' : 'block';
  }
  if (instructionCount === 0) return;
  const template = document.getElementById('customInstructionItemTemplate');
  Object.entries(customInstructions).forEach(([id, instruction]) => {
    if (template) {
      // Use the template if available
      const clone = template.content.cloneNode(true);
      const item = clone.querySelector('.custom-instruction-item');
      item.dataset.id = id;
      const titleElem = item.querySelector('.custom-instruction-title');
      if (titleElem) titleElem.textContent = instruction.title || id;
      const categoriesElem = item.querySelector(
        '.custom-instruction-categories'
      );
      if (
        categoriesElem &&
        instruction.categories &&
        instruction.categories.length > 0
      ) {
        instruction.categories.forEach((category) => {
          const categorySpan = document.createElement('span');
          categorySpan.className = 'custom-instruction-category';
          categorySpan.textContent = category;
          categoriesElem.appendChild(categorySpan);
        });
      }
      const exportBtn = item.querySelector('.custom-instruction-action.export');
      const editBtn = item.querySelector('.custom-instruction-action.edit');
      const deleteBtn = item.querySelector('.custom-instruction-action.delete');
      if (exportBtn) {
        exportBtn.addEventListener('click', () => exportInstruction(id));
      }
      if (editBtn) {
        editBtn.addEventListener('click', () => showEditInstructionModal(id));
      }
      if (deleteBtn) {
        deleteBtn.addEventListener('click', () => showDeleteConfirmation(id));
      }
      container.appendChild(item);
    } else {
      // Fallback if template not available
      const item = document.createElement('div');
      item.className = 'custom-instruction-item';
      item.dataset.id = id;
      const info = document.createElement('div');
      info.className = 'custom-instruction-info';
      const title = document.createElement('div');
      title.className = 'custom-instruction-title';
      title.textContent = instruction.title || id;
      info.appendChild(title);
      if (instruction.categories && instruction.categories.length > 0) {
        const categoriesContainer = document.createElement('div');
        categoriesContainer.className = 'custom-instruction-categories';
        instruction.categories.forEach((category) => {
          const categorySpan = document.createElement('span');
          categorySpan.className = 'custom-instruction-category';
          categorySpan.textContent = category;
          categoriesContainer.appendChild(categorySpan);
        });
        info.appendChild(categoriesContainer);
      }
      const actions = document.createElement('div');
      actions.className = 'custom-instruction-actions';
      // Export button
      const exportBtn = document.createElement('button');
      exportBtn.className = 'custom-instruction-action export';
      exportBtn.innerHTML = '<i class="fas fa-download"></i>';
      exportBtn.title = 'Export';
      exportBtn.addEventListener('click', () => exportInstruction(id));
      actions.appendChild(exportBtn);
      // Edit button
      const editBtn = document.createElement('button');
      editBtn.className = 'custom-instruction-action edit';
      editBtn.innerHTML = '<i class="fas fa-edit"></i>';
      editBtn.title = 'Edit';
      editBtn.addEventListener('click', () => showEditInstructionModal(id));
      actions.appendChild(editBtn);
      // Delete button
      const deleteBtn = document.createElement('button');
      deleteBtn.className = 'custom-instruction-action delete';
      deleteBtn.innerHTML = '<i class="fas fa-trash"></i>';
      deleteBtn.title = 'Delete';
      deleteBtn.addEventListener('click', () => showDeleteConfirmation(id));
      actions.appendChild(deleteBtn);
      item.appendChild(info);
      item.appendChild(actions);
      container.appendChild(item);
    }
  });
}

/**
 * Shows the custom instruction modal for creating or editing
 * @param {string|null} id - Instruction ID (null for new instructions)
 */
function showCustomInstructionModal(id = null) {
  // Get modal elements
  const modal = document.getElementById('customInstructionModal');
  const title = modal.querySelector('.modal-title');
  const form = document.getElementById('customInstructionForm');
  const idInput = document.getElementById('instructionId');
  const titleInput = document.getElementById('instructionTitle');
  const tooltipInput = document.getElementById('instructionTooltip');
  const contentTextarea = document.getElementById('instructionContent');
  const saveBtn = document.getElementById('saveCustomInstructionBtn');
  // Reset form fields
  form.reset();
  // Set manual entry mode by default
  document.getElementById('manualEntryBtn').checked = true;
  // Clear generated content fields
  const generatedContent = document.getElementById(
    'generatedInstructionContent'
  );
  if (generatedContent) {
    generatedContent.value = '';
  }
  const resultElement = document.querySelector('.ai-generation-result');
  if (resultElement) {
    resultElement.style.display = 'none';
  }
  const statusElement = document.querySelector('.ai-generation-status');
  if (statusElement) {
    statusElement.style.display = 'none';
  }
  // Show manual entry fields, hide AI generation fields
  document.getElementById('manualEntryFields').style.display = 'block';
  document.getElementById('aiGenerationFields').style.display = 'none';
  // Show save button, hide generate button
  saveBtn.style.display = 'block';
  document.getElementById('generateWithAIBtn').style.display = 'none';
  // Clear category checkboxes to ensure no old values are carried over
  const categoryCheckboxes = form.querySelectorAll(
    'input[name="instructionCategory"]'
  );
  categoryCheckboxes.forEach((checkbox) => {
    checkbox.checked = false;
  });
  if (id) {
    const instruction = MP.settings.instructions?.custom?.[id];
    if (!instruction) {
      console.error(`Custom instruction "${id}" not found`);
      return;
    }
    title.textContent = 'Edit Custom Instruction';
    idInput.value = id;
    titleInput.value = instruction.title || '';
    tooltipInput.value = instruction.tooltip || '';
    contentTextarea.value = instruction.content || '';
    if (Array.isArray(instruction.categories)) {
      instruction.categories.forEach((category) => {
        const checkbox = form.querySelector(
          `input[name="instructionCategory"][value="${category}"]`
        );
        if (checkbox) {
          checkbox.checked = true;
        }
      });
    }
  } else {
    title.textContent = 'Add Custom Instruction'; // Add new instruction
    idInput.value = '';
  }
  // Show modal and initialize creation mode toggle
  const bootstrapModal = new bootstrap.Modal(modal);
  bootstrapModal.show();
  // Initialize the toggle functionality
  toggleInstructionCreationMode();
  // Set up save button handler
  if (saveBtn) {
    const newSaveBtn = saveBtn.cloneNode(true);
    saveBtn.parentNode.replaceChild(newSaveBtn, saveBtn);
    newSaveBtn.addEventListener('click', saveCustomInstructionFromForm);
  }
}

/**
 * Shows delete confirmation modal
 * @param {string} id - Instruction ID to delete
 */
function showDeleteConfirmation(id) {
  if (!id || !MP.settings.instructions?.custom?.[id]) return;
  const modal = document.getElementById('confirmDeleteModal');
  const confirmBtn = document.getElementById('confirmDeleteBtn');
  if (!modal || !confirmBtn) return;
  // Create new button to remove old event listeners
  const newConfirmBtn = confirmBtn.cloneNode(true);
  confirmBtn.parentNode.replaceChild(newConfirmBtn, confirmBtn);
  newConfirmBtn.addEventListener('click', () => {
    deleteCustomInstruction(id);
    bootstrap.Modal.getInstance(modal).hide();
  });
  const bootstrapModal = new bootstrap.Modal(modal);
  bootstrapModal.show();
}

/**
 * Handles toggling between manual entry and AI generation
 */
function toggleInstructionCreationMode() {
  const manualEntryBtn = document.getElementById('manualEntryBtn');
  const aiGenerateBtn = document.getElementById('aiGenerateBtn');
  const manualEntryFields = document.getElementById('manualEntryFields');
  const aiGenerationFields = document.getElementById('aiGenerationFields');
  const generateWithAIBtn = document.getElementById('generateWithAIBtn');
  const saveCustomInstructionBtn = document.getElementById(
    'saveCustomInstructionBtn'
  );
  // Setup event listeners for the toggle buttons
  manualEntryBtn.addEventListener('change', updateDisplayMode);
  aiGenerateBtn.addEventListener('change', updateDisplayMode);
  // Generate button handler
  generateWithAIBtn.addEventListener('click', generateInstructionWithAI);
  // Regenerate button handler
  document
    .getElementById('regenerateInstructionBtn')
    ?.addEventListener('click', regenerateInstruction);

  function updateDisplayMode() {
    if (manualEntryBtn.checked) {
      // Show manual entry, hide AI generation
      manualEntryFields.style.display = 'block';
      aiGenerationFields.style.display = 'none';
      generateWithAIBtn.style.display = 'none';
      saveCustomInstructionBtn.style.display = 'block';
    } else if (aiGenerateBtn.checked) {
      // Show AI generation, hide manual entry
      manualEntryFields.style.display = 'none';
      aiGenerationFields.style.display = 'block';
      // Check if we already have generated content
      const generatedContent = document.getElementById(
        'generatedInstructionContent'
      );
      if (generatedContent && generatedContent.value.trim()) {
        document.querySelector('.ai-generation-result').style.display = 'block';
        generateWithAIBtn.style.display = 'none';
      } else {
        document.querySelector('.ai-generation-result').style.display = 'none';
        generateWithAIBtn.style.display = 'block';
      }
      document.querySelector('.ai-generation-status').style.display = 'none';
    }
  }
  // Initialize display
  updateDisplayMode();
}

/**
 * Generate instruction using AI based on user description
 */
async function generateInstructionWithAI() {
  const descriptionTextarea = document.getElementById('aiPromptDescription');
  const description = descriptionTextarea.value.trim();
  if (!description) {
    console.error(
      'Please enter a description of what you want the instruction to accomplish'
    );
    alert(
      'Please enter a description of what you want the instruction to accomplish'
    );
    return;
  }
  // Get selected categories to determine how the AI will create the instructions
  const selectedCategories = Array.from(
    document.querySelectorAll('input[name="instructionCategory"]:checked')
  ).map((checkbox) => checkbox.value);
  if (selectedCategories.length === 0) {
    console.error('Please select at least one category');
    alert('Please select at least one category');
    return;
  }
  try {
    // Show generating status using an animation
    const statusElement = document.querySelector('.ai-generation-status');
    const resultElement = document.querySelector('.ai-generation-result');
    const generateButton = document.getElementById('generateWithAIBtn');
    const saveButton = document.getElementById('saveCustomInstructionBtn');
    statusElement.style.display = 'block';
    resultElement.style.display = 'none';
    generateButton.disabled = true;
    saveButton.disabled = true;
    // Create enhanced prompt for the AI
    const prompt = `
          I need you to create a SYSTEM PROMPT that will be given to an AI language model.

          This system prompt should be based on this description:
          "${description}"
  
          For these categories: ${selectedCategories.join(', ')}
  
          Category purposes:
          - Chat category: The AI responds to general user questions and conversations
          - Vision category: The AI analyzes uploaded images and provides descriptions
          - Caption category: The AI generates Stable Diffusion prompts from images
          - Prompt category: The AI enhances user text into detailed image generation prompts
  
          IMPORTANT: A system prompt is a set of instructions GIVEN TO AN AI, not instructions for humans.
          It should be written in second person ("You are...", "Your goal is...") addressing the AI directly.
  
          Your response must contain ONLY the system prompt text itself with no bullet points, headings, or explanations.
          Bad example: "System Prompt: You are an AI that..."
          Good example: "You are an AI that..."
        `;
    // Make API request using the current LLM backend
    const payload = MP.RequestBuilder.createRequestPayload(
      prompt,
      null,
      'generate-instruction'
    );
    const response = await MP.APIClient.makeRequest(payload);
    // Hide status, show result
    statusElement.style.display = 'none';
    generateButton.disabled = false;
    saveButton.disabled = false;
    if (response.success && response.response) {
      // Update the generated content textarea
      const generatedContent = document.getElementById(
        'generatedInstructionContent'
      );
      generatedContent.value = response.response.trim();
      // Also update the hidden instructionContent field that will be used for saving
      const instructionContent = document.getElementById('instructionContent');
      instructionContent.value = response.response.trim();
      // Show the result section
      resultElement.style.display = 'block';
      generateButton.style.display = 'none';
      saveButton.style.display = 'block';
    } else {
      throw new Error(response.error || 'Failed to generate instruction');
    }
  } catch (error) {
    console.error('Instruction generation error:', error);
    alert(`Failed to generate instruction: ${error.message}`);
    // Reset UI
    document.querySelector('.ai-generation-status').style.display = 'none';
    document.getElementById('generateWithAIBtn').disabled = false;
    document.getElementById('saveCustomInstructionBtn').disabled = false;
  }
}

/**
 * Regenerate the instruction with a new AI call
 */
function regenerateInstruction() {
  // Clear the generated content
  document.getElementById('generatedInstructionContent').value = '';
  // Hide result section
  document.querySelector('.ai-generation-result').style.display = 'none';
  // Show generate button
  document.getElementById('generateWithAIBtn').style.display = 'block';
  // Generate new instruction
  generateInstructionWithAI();
}

/**
 * Saves a custom instruction from the form data
 */
function saveCustomInstructionFromForm() {
  const form = document.getElementById('customInstructionForm');
  const idInput = document.getElementById('instructionId');
  const titleInput = document.getElementById('instructionTitle');
  const tooltipInput = document.getElementById('instructionTooltip');
  // Get content based on selected mode
  let contentValue = '';
  if (document.getElementById('manualEntryBtn').checked) {
    contentValue = document.getElementById('instructionContent').value.trim();
  } else {
    contentValue = document
      .getElementById('generatedInstructionContent')
      .value.trim();
  }
  if (!form || !titleInput) {
    console.error('Form elements not found');
    return;
  }
  // Validate required fields (trim to remove whitespace)
  if (!titleInput.value.trim()) {
    alert('Please enter a title for the instruction');
    titleInput.focus();
    return;
  }
  if (!contentValue) {
    if (document.getElementById('manualEntryBtn').checked) {
      alert('Please enter instruction content');
      document.getElementById('instructionContent').focus();
    } else {
      alert('Please generate instruction content first');
      document.getElementById('generateWithAIBtn').focus();
    }
    return;
  }
  const categoryCheckboxes = form.querySelectorAll(
    'input[name="instructionCategory"]:checked'
  );
  if (categoryCheckboxes.length === 0) {
    alert('Please select at least one category');
    return;
  }
  const categories = Array.from(categoryCheckboxes).map(
    (checkbox) => checkbox.value
  );
  const data = {
    title: titleInput.value.trim(),
    content: contentValue,
    tooltip: tooltipInput.value.trim(),
    categories,
  };
  const id = idInput.value; // Get ID (if editing)
  if (id) {
    updateCustomInstruction(id, data);
  } else {
    addCustomInstruction(data);
  }
  populateFeatureSelects(); // Refresh feature selects
  const modal = document.getElementById('customInstructionModal');
  bootstrap.Modal.getInstance(modal).hide();
}

/**
 * Exports a single custom instruction as a JSON file
 * @param {string} id - Instruction ID to export
 */
function exportInstruction(id) {
  if (!id || !MP.settings.instructions?.custom?.[id]) {
    console.error(`Custom instruction "${id}" not found`);
    return;
  }
  const instruction = MP.settings.instructions.custom[id];
  // Create JSON blob and trigger download
  const json = JSON.stringify(instruction, null, 2);
  const blob = new Blob([json], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `instruction-${instruction.title || id}.json`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a); // Clean up
  URL.revokeObjectURL(url);
}

/**
 * Imports custom instructions from a JSON file
 * @param {File} file - JSON file to import
 */
function importCustomInstructions(file) {
  if (!file) return;
  const reader = new FileReader();
  reader.onload = (e) => {
    try {
      const data = JSON.parse(e.target.result);
      let imported = 0;
      let skipped = 0;
      Object.entries(data).forEach(([id, instruction]) => {
        if (!instruction.title || !instruction.content) {
          console.warn(`Skipping invalid instruction: ${id}`);
          skipped++;
          return;
        }
        // Generate new ID to avoid conflicts and mark as imported
        const newId = `custom-${Date.now()}-${imported}`;
        addCustomInstruction({
          id: newId,
          title: instruction.title,
          content: instruction.content,
          tooltip: instruction.tooltip,
          categories: instruction.categories || [],
        });
        imported++;
      });
      alert(
        `Successfully imported ${imported} custom instructions${
          skipped > 0 ? ` (${skipped} skipped)` : ''
        }`
      );
      populateFeatureSelects(); // Refresh feature selects
    } catch (error) {
      console.error('Error importing instructions:', error);
      alert(`Error importing instructions: ${error.message}`);
    }
  };
  reader.readAsText(file);
}

/**
 * Shows the edit modal for a custom instruction
 * @param {string} id - The ID of the instruction to edit
 */
function showEditInstructionModal(id) {
  if (!id || !MP.settings.instructions?.custom?.[id]) {
    console.error(`Custom instruction "${id}" not found`);
    return;
  }
  // Call the existing showCustomInstructionModal function with the ID
  showCustomInstructionModal(id);
}

/**
 * Initialize instructions UI with radio buttons and a single textarea
 * Manages switching between different instruction types
 */
function initInstructionsUI() {
  // Get references to elements
  const instructionTypeRadios = document.querySelectorAll(
    'input[name="instructionType"]'
  );
  const instructionTextarea = document.getElementById('instructionTextarea');
  const instructionLabel = document.getElementById('instructionLabel');
  const instructionHelpText = document.getElementById('instructionHelpText');
  if (!instructionTextarea || !instructionLabel || !instructionHelpText) {
    console.error('Instruction UI elements not found');
    return;
  }
  window.instructionTypes = {
    chat: {
      title: 'Chat Instructions',
      tooltip:
        "Instructions for how the AI should behave in chat conversations. These define the AI's personality and response style.",
      helpText: 'Define how the AI should behave in chat conversations',
      placeholder: "Configure the AI's chat personality and behavior",
    },
    vision: {
      title: 'Vision Instructions',
      tooltip:
        'Instructions for how the AI should analyze and describe images in the vision tab. These instructions are used when discussing images.',
      helpText: 'Guide how the AI analyzes and describes images',
      placeholder: 'Configure how the AI should analyze and describe images',
    },
    caption: {
      title: 'Image Caption Instructions',
      tooltip:
        "Instructions for how the AI should generate image captions. These instructions are used when clicking the 'Caption' button in the vision tab.",
      helpText: 'Define how the AI generates captions for images',
      placeholder: 'Configure how the AI should generate image captions',
    },
    prompt: {
      title: 'Enhance Prompt Instructions',
      tooltip:
        "Instructions for how the AI should format text-to-image prompts when generating images. These instructions are used when clicking 'Enhance Prompt' in the generate tab.",
      helpText:
        'These instructions guide how the AI formats and enhances prompts',
      placeholder: 'How do you want the AI to Enhance your prompt?',
    },
  };
  // Add custom instructions from saved user settings
  if (MP.settings.instructions?.custom) {
    Object.entries(MP.settings.instructions.custom).forEach(
      ([id, instruction]) => {
        addCustomInstructionToUI(id, instruction); // Add to radio buttons and instruction types
        instructionTypes[id] = {
          title: instruction.title || id,
          tooltip:
            instruction.tooltip ||
            `Custom instructions for ${instruction.title || id}`,
          helpText: `Custom instructions for ${
            Array.isArray(instruction.categories)
              ? instruction.categories.join(', ')
              : 'custom use'
          }`,
          placeholder: `Enter custom instructions for ${
            instruction.title || id
          }`,
        };
      }
    );
  }

  // Update the instruction UI based on selected type
  function updateInstructionUI(type) {
    const typeConfig = instructionTypes[type];
    if (!typeConfig) {
      console.error(`Instruction type "${type}" not found`);
      return;
    }
    // Update the label, tooltip, and help text
    if (instructionLabel) {
      let textNode = Array.from(instructionLabel.childNodes).find(
        (node) => node.nodeType === Node.TEXT_NODE
      );
      if (!textNode) {
        textNode = document.createTextNode('');
        instructionLabel.insertBefore(textNode, instructionLabel.firstChild);
      }
      textNode.nodeValue = typeConfig.title + ' ';
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
    if (instructionHelpText) {
      instructionHelpText.textContent = typeConfig.helpText;
    }
    if (instructionTextarea) {
      instructionTextarea.placeholder = typeConfig.placeholder;
      const value = getInstructionContent(type);
      // Set the textarea value and force a refresh
      instructionTextarea.value = value;
      setTimeout(() => {
        const event = new Event('input', { bubbles: true });
        instructionTextarea.dispatchEvent(event);
      }, 0);
    }
  }

  // Function to handle radio button changes
  function handleInstructionChange(e) {
    if (e.target.checked) {
      // Only save content if the textarea exists
      if (instructionTextarea) {
        const currentType = Array.from(instructionTypeRadios)
          .find((r) => r.checked && r !== e.target)
          ?.id.replace('InstructionBtn', '')
          .toLowerCase();
        if (currentType) {
          saveCurrentInstructionContent(currentType);
        }
      }
      const type = e.target.id.replace('InstructionBtn', '').toLowerCase();
      updateInstructionUI(type);
    }
  }

  // Function to save the current instruction content
  function saveCurrentInstructionContent(specificType) {
    if (!instructionTextarea) return;
    const selectedType =
      specificType ||
      Array.from(instructionTypeRadios)
        .find((r) => r.checked)
        ?.id.replace('InstructionBtn', '')
        .toLowerCase();
    if (!selectedType) return;
    if (
      selectedType === 'chat' ||
      selectedType === 'vision' ||
      selectedType === 'caption' ||
      selectedType === 'prompt'
    ) {
      // Only save if the content has actually changed
      if (
        MP.settings.instructions[selectedType] !== instructionTextarea.value
      ) {
        MP.settings.instructions[selectedType] = instructionTextarea.value;
      }
    } else if (MP.settings.instructions?.custom) {
      // This is a custom instruction so we need to ensure the object exists
      if (!MP.settings.instructions.custom[selectedType]) {
        MP.settings.instructions.custom[selectedType] = {};
      }
      const currentContent =
        typeof MP.settings.instructions.custom[selectedType] === 'object'
          ? MP.settings.instructions.custom[selectedType].content || ''
          : MP.settings.instructions.custom[selectedType] || '';
      // Only save if changed
      if (currentContent !== instructionTextarea.value) {
        if (typeof MP.settings.instructions.custom[selectedType] === 'object') {
          MP.settings.instructions.custom[selectedType].content =
            instructionTextarea.value;
        } else {
          MP.settings.instructions.custom[selectedType] =
            instructionTextarea.value;
        }
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
          .find((r) => r.checked)
          ?.id.replace('InstructionBtn', '')
          .toLowerCase();
        if (selectedType) {
          saveCurrentInstructionContent(selectedType);
        }
        saveTimeout = null;
      }, 500); // Wait 500ms after typing stops
    });
  }
  instructionTypeRadios.forEach((radio) => {
    radio.addEventListener('change', handleInstructionChange);
  });
  // Ensure instructions are properly initialized
  if (!MP.settings.instructions) {
    MP.settings.instructions = {
      chat: '',
      vision: '',
      caption: '',
      prompt: '',
      custom: {},
      featureMap: { ...DEFAULT_FEATURE_MAPPINGS },
    };
  }
  // Initialize with the selected instruction type (default to chat)
  const selectedType =
    Array.from(instructionTypeRadios)
      .find((r) => r.checked)
      ?.id.replace('InstructionBtn', '')
      .toLowerCase() || 'chat';
  // Make sure the correct radio button is checked
  const selectedRadio = document.getElementById(
    `${selectedType}InstructionBtn`
  );
  if (selectedRadio) {
    selectedRadio.checked = true;
  }
  updateInstructionUI(selectedType);
  renderCustomInstructionsList();
  populateFeatureSelects();
  // Set up instruction management buttons
  const addCustomInstructionBtn = document.getElementById(
    'addCustomInstructionBtn'
  );
  if (addCustomInstructionBtn) {
    addCustomInstructionBtn.addEventListener('click', () =>
      showCustomInstructionModal()
    );
  }
  const importInstructionsBtn = document.getElementById(
    'importInstructionsBtn'
  );
  const importInstructionsInput = document.getElementById(
    'importInstructionsInput'
  );
  if (importInstructionsBtn && importInstructionsInput) {
    importInstructionsBtn.addEventListener('click', () => {
      importInstructionsInput.click();
    });
    importInstructionsInput.addEventListener('change', (e) => {
      if (e.target.files.length > 0) {
        importCustomInstructions(e.target.files[0]);
        e.target.value = ''; // Reset input
      }
    });
  }
  return {
    updateInstructionUI,
    saveCurrentInstructionContent,
    instructionTypes,
    handleInstructionChange,
  };
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
 * Initialize instructions tab interface
 */
function initInstructionsTabInterface() {
  // Set up tab switching to retain state
  const tabEl = document.getElementById('instructionsTabNav');
  if (tabEl) {
    const triggerTabList = tabEl.querySelectorAll('button');
    triggerTabList.forEach((triggerEl) => {
      triggerEl.addEventListener('click', (event) => {
        event.preventDefault();
        // Activate this tab
        const tab = new bootstrap.Tab(triggerEl);
        tab.show();
      });
    });
  }
  // Prevent header buttons from triggering collapse
  document.querySelectorAll('.header-buttons button').forEach((button) => {
    button.addEventListener('click', (e) => {
      e.stopPropagation();
    });
  });
  // Initialize auto-resize for textareas
  const textareas = document.querySelectorAll(
    '#settingsModal textarea, #customInstructionModal textarea'
  );
  textareas.forEach((textarea) => {
    textarea.addEventListener('input', function () {
      this.style.height = 'auto';
      const maxHeight = parseInt(
        getComputedStyle(this).getPropertyValue('max-height'),
        10
      );
      const scrollHeight = this.scrollHeight;
      if (scrollHeight <= maxHeight) {
        this.style.height = scrollHeight + 'px';
      } else {
        this.style.height = maxHeight + 'px';
      }
    });
    // Initial resize
    if (textarea.offsetParent !== null) {
      // Only process visible textareas
      textarea.dispatchEvent(new Event('input'));
    }
  });
  // Initialize the custom instruction modal
  initCustomInstructionModal();
  // Replace the saveCustomInstructionBtn event handler with our enhanced version
  const saveBtn = document.getElementById('saveCustomInstructionBtn');
  if (saveBtn) {
    const newSaveBtn = saveBtn.cloneNode(true);
    saveBtn.parentNode.replaceChild(newSaveBtn, saveBtn);
    newSaveBtn.addEventListener('click', saveCustomInstructionFromForm);
  }
}

/**
 * Initializes settings modal with saved values
 */
function initSettingsModal() {
  try {
    // Ensure the instructions object has the correct structure
    if (!MP.settings.instructions) {
      MP.settings.instructions = {
        chat: '',
        vision: '',
        caption: '',
        prompt: '',
        custom: {},
        featureMap: { ...DEFAULT_FEATURE_MAPPINGS },
      };
    } else {
      if (!MP.settings.instructions.custom) {
        MP.settings.instructions.custom = {};
      }
      if (!MP.settings.instructions.featureMap) {
        MP.settings.instructions.featureMap = { ...DEFAULT_FEATURE_MAPPINGS };
      }
    }
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
    const currentBackendRadio = document.getElementById(
      `${currentBackend}LLMBtn`
    );
    if (currentBackendRadio) {
      currentBackendRadio.checked = true;
    }
    updateBaseUrlVisibility(currentBackend, false);
    const backendUrl = document.getElementById('backendUrl');
    if (backendUrl) {
      backendUrl.value = MP.settings.backends[currentBackend]?.baseurl || '';
    }
    const currentVisionBackend = MP.settings.visionbackend || 'ollama';
    const currentVisionBackendRadio = document.getElementById(
      `${currentVisionBackend}VisionBtn`
    );
    if (currentVisionBackendRadio) {
      currentVisionBackendRadio.checked = true;
    }
    updateBaseUrlVisibility(currentVisionBackend, true);
    const visionBackendUrl = document.getElementById('visionBackendUrl');
    if (visionBackendUrl) {
      visionBackendUrl.value =
        MP.settings.backends[currentVisionBackend]?.baseurl || '';
    }
    initInstructionsUI();
    initInstructionsTabInterface();

    // Show loading state before fetching models
    const modelSelect = document.getElementById('modelSelect');
    const visionModelSelect = document.getElementById('visionModel');
    if (modelSelect) {
      modelSelect.innerHTML = '';
      const loadingOption = new Option(
        `Loading ${currentBackend} models...`,
        ''
      );
      loadingOption.disabled = true;
      modelSelect.add(loadingOption);
    }
    if (visionModelSelect) {
      visionModelSelect.innerHTML = '';
      const loadingOption = new Option(
        `Loading ${currentVisionBackend} vision models...`,
        ''
      );
      loadingOption.disabled = true;
      visionModelSelect.add(loadingOption);
    }
    fetchModels()
      .then((result) => {
        console.log('Initial models loaded:', result);
      })
      .catch((error) => {
        console.error('Error loading initial models:', error);
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
    // Add event listeners for chat backend selection
    const linkingBackendRadios = document.querySelectorAll(
      'input[name="llmBackend"]'
    );
    linkingBackendRadios.forEach((radio) => {
      radio.addEventListener('change', async (e) => {
        const backend = e.target.id.replace('LLMBtn', '').toLowerCase();
        updateBaseUrlVisibility(backend, false);
        const modelSelect = document.getElementById('modelSelect');
        if (modelSelect) {
          modelSelect.innerHTML = '';
          const loadingOption = new Option(`Loading ${backend} models...`, '');
          loadingOption.disabled = true;
          modelSelect.add(loadingOption);
        }
        try {
          const backendUrl = document.getElementById('backendUrl');
          const baseUrl = backendUrl ? backendUrl.value : '';
          MP.settings.backend = backend;
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
                ...MP.settings.backends,
              },
            },
          };
          // Use Swarm's built in genericRequest which will automatically add session_id
          await new Promise((resolve, reject) => {
            genericRequest('SaveMagicPromptSettings', payload, (data) => {
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
          await fetchModels();
        } catch (error) {
          console.error(`Error fetching models for ${backend}:`, error);
          if (modelSelect) {
            modelSelect.innerHTML = '';
            const errorOption = new Option(
              `Error loading models: ${error.message}`,
              ''
            );
            errorOption.disabled = true;
            modelSelect.add(errorOption);
          }
        }
      });
    });
    // Add event listeners for vision backend selection
    const visionBackendRadios = document.querySelectorAll(
      'input[name="visionBackendSelect"]'
    );
    visionBackendRadios.forEach((radio) => {
      radio.addEventListener('change', async (e) => {
        const backend = e.target.id.replace('VisionBtn', '').toLowerCase();
        updateBaseUrlVisibility(backend, true);
        const modelSelect = document.getElementById('visionModel');
        if (modelSelect) {
          modelSelect.innerHTML = '';
          const loadingOption = new Option(
            `Loading ${backend} vision models...`,
            ''
          );
          loadingOption.disabled = true;
          modelSelect.add(loadingOption);
        }
        try {
          const backendUrl = document.getElementById('visionBackendUrl');
          const baseUrl = backendUrl ? backendUrl.value : '';
          MP.settings.visionbackend = backend;
          if (baseUrl && needsBaseUrl(backend)) {
            if (!MP.settings.backends[backend]) {
              MP.settings.backends[backend] = {};
            }
            MP.settings.backends[backend].baseurl = baseUrl;
          }
          const payload = {
            settings: {
              visionbackend: backend,
              backends: {
                ...MP.settings.backends,
              },
            },
          };
          await new Promise((resolve, reject) => {
            genericRequest('SaveMagicPromptSettings', payload, (data) => {
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
          await fetchModels();
        } catch (error) {
          console.error(`Error fetching vision models for ${backend}:`, error);
          if (modelSelect) {
            modelSelect.innerHTML = '';
            const errorOption = new Option(
              `Error loading vision models: ${error.message}`,
              ''
            );
            errorOption.disabled = true;
            modelSelect.add(errorOption);
          }
        }
      });
    });
    // Add event listeners for base URL changes with debounce
    const backendBaseUrl = document.getElementById('backendUrl');
    if (backendBaseUrl) {
      // Create a debounced save function
      const debouncedSaveUrl = debounce(async function (value) {
        try {
          const currentBackend =
            document
              .querySelector('input[name="llmBackend"]:checked')
              ?.id.replace('LLMBtn', '')
              .toLowerCase() || MP.settings.backend;
          if (needsBaseUrl(currentBackend)) {
            // Update settings
            if (!MP.settings.backends[currentBackend]) {
              MP.settings.backends[currentBackend] = {};
            }
            MP.settings.backends[currentBackend].baseurl = value;
            // Create minimal payload
            const payload = {
              settings: {
                backends: {
                  [currentBackend]: {
                    baseurl: value,
                  },
                },
              },
            };
            // Save settings
            await new Promise((resolve, reject) => {
              genericRequest('SaveMagicPromptSettings', payload, (data) => {
                  if (data.success) {
                    console.log(
                      `Saved base URL for ${currentBackend}: ${value}`
                    );
                    resolve();
                  } else {
                    console.error(
                      `Failed to save base URL: ${
                        data.error || 'Unknown error'
                      }`
                    );
                    reject(new Error(data.error || 'Failed to save base URL'));
                  }
                },
                0,
                (error) => {
                  console.error('Error saving base URL:', error);
                  reject(error);
                }
              );
            });
            // Fetch models with new URL
            await fetchModels();
          }
        } catch (error) {
          console.error('Error saving base URL:', error);
        }
      }, 1000); // 1 second debounce
      // Add input event listener with debounce
      backendBaseUrl.addEventListener('input', function (e) {
        // Sync with vision URL when linked
        if (document.getElementById('linkModelsToggle')?.checked) {
          const visionBackendUrl = document.getElementById('visionBackendUrl');
          if (visionBackendUrl) {
            visionBackendUrl.value = e.target.value;
          }
        }
        // Show loading indication in the model select
        const modelSelect = document.getElementById('modelSelect');
        if (modelSelect) {
          modelSelect.innerHTML = '';
          const loadingOption = new Option(
            'URL changed, models will update soon...',
            ''
          );
          loadingOption.disabled = true;
          modelSelect.add(loadingOption);
        }
        // Trigger the debounced save
        debouncedSaveUrl(e.target.value);
      });
    }
    // Add event listeners for vision base URL changes with debounce
    const visionBackendUrlInput = document.getElementById('visionBackendUrl');
    if (visionBackendUrlInput) {
      // Create a debounced save function
      const debouncedSaveVisionUrl = debounce(async function (value) {
        try {
          const currentVisionBackend =
            document
              .querySelector('input[name="visionBackendSelect"]:checked')
              ?.id.replace('VisionBtn', '')
              .toLowerCase() || MP.settings.visionbackend;
          if (needsBaseUrl(currentVisionBackend)) {
            // Update settings
            if (!MP.settings.backends[currentVisionBackend]) {
              MP.settings.backends[currentVisionBackend] = {};
            }
            MP.settings.backends[currentVisionBackend].baseurl = value;
            // Create minimal payload
            const payload = {
              settings: {
                backends: {
                  [currentVisionBackend]: {
                    baseurl: value,
                  },
                },
              },
            };
            // Save settings
            await new Promise((resolve, reject) => {
              genericRequest('SaveMagicPromptSettings', payload, (data) => {
                  if (data.success) {
                    console.log(
                      `Saved vision base URL for ${currentVisionBackend}: ${value}`
                    );
                    resolve();
                  } else {
                    console.error(
                      `Failed to save vision base URL: ${
                        data.error || 'Unknown error'
                      }`
                    );
                    reject(
                      new Error(data.error || 'Failed to save vision base URL')
                    );
                  }
                },
                0,
                (error) => {
                  console.error('Error saving vision base URL:', error);
                  reject(error);
                }
              );
            });
            // Fetch models with new URL
            await fetchModels();
          }
        } catch (error) {
          console.error('Error saving vision base URL:', error);
        }
      }, 1000); // 1 second debounce
      // Add input event listener with debounce
      visionBackendUrlInput.addEventListener('input', function (e) {
        // Show loading indication in the model select
        const visionModelSelect = document.getElementById('visionModel');
        if (visionModelSelect) {
          visionModelSelect.innerHTML = '';
          const loadingOption = new Option(
            'URL changed, models will update soon...',
            ''
          );
          loadingOption.disabled = true;
          visionModelSelect.add(loadingOption);
        }
        // Trigger the debounced save
        debouncedSaveVisionUrl(e.target.value);
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
    const modelDropdown = document.getElementById('modelSelect');
    if (modelDropdown) {
      modelDropdown.addEventListener('change', function (e) {
        if (document.getElementById('linkModelsToggle')?.checked) {
          const visionModelSelect = document.getElementById('visionModel');
          if (visionModelSelect) {
            visionModelSelect.value = modelDropdown.value;
          }
        }
      });
    }
    // Add event listeners for backend selection
    const linkBackendRadios = document.querySelectorAll(
      'input[name="llmBackend"]'
    );
    linkBackendRadios.forEach((radio) => {
      radio.addEventListener('change', function (e) {
        if (document.getElementById('linkModelsToggle')?.checked) {
          const backend = e.target.id.replace('LLMBtn', '').toLowerCase();
          const visionBackendRadio = document.getElementById(
            `${backend}VisionBtn`
          );
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
    // showError('Error initializing settings modal:', error);
  }
}

// Add this function at the end of settings.js
function showSettingsModal() {
  try {
    const modal = document.getElementById('settingsModal');
    if (!modal) return;
    // Check if there's already an instance
    let modalInstance = bootstrap.Modal.getInstance(modal);
    // If modal is already visible, do nothing
    if (modal.classList.contains('show')) {
      return;
    }
    // Initialize modal contents
    initSettingsModal();
    // Use existing instance or create a new one
    if (modalInstance) {
      modalInstance.show();
    } else {
      // Only create a new instance if one doesn't exist
      modalInstance = new bootstrap.Modal(modal);
      modalInstance.show();
    }
  } catch (error) {
    console.error('Error showing settings modal:', error);
  }
}

/**
 * Updates UI for linked/unlinked chat and vision models
 * @param {boolean} isLinked - Whether models are linked
 */
function updateLinkedModelsUI(isLinked) {
  try {
    // Get UI elements
    const visionSettingsCard = document.querySelector(
      '.settings-card:nth-child(2)'
    );
    const chatSettingsCard = document.querySelector(
      '.settings-card:first-child'
    );
    const visionContent = document.getElementById('visionSettingsCollapse');
    const chatContent = document.getElementById('chatSettingsCollapse');
    const visionHeader = visionSettingsCard?.querySelector(
      '.settings-section-title'
    );
    const chatHeader = chatSettingsCard?.querySelector(
      '.settings-section-title'
    );
    // If elements don't exist, exit early
    if (!visionSettingsCard || !chatSettingsCard) return;
    // Get model selects
    const chatModelSelect = document.getElementById('modelSelect');
    const visionModelSelect = document.getElementById('visionModel');
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
      const selectedChatBackend = document.querySelector(
        'input[name="llmBackend"]:checked'
      );
      if (selectedChatBackend) {
        const chatBackendId = selectedChatBackend.id
          .replace('LLMBtn', '')
          .toLowerCase();
        const visionBackendRadio = document.getElementById(
          `${chatBackendId}VisionBtn`
        );
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

// Initialize custom instruction modal
function initCustomInstructionModal() {
  const modal = document.getElementById('customInstructionModal');
  // Set up the creation mode toggle when the modal opens
  modal.addEventListener('show.bs.modal', () => {
    // Reset mode to manual entry
    document.getElementById('manualEntryBtn').checked = true;
    // Initialize the toggle functionality
    toggleInstructionCreationMode();
    // Clear generated content fields
    const generatedContent = document.getElementById(
      'generatedInstructionContent'
    );
    if (generatedContent) {
      generatedContent.value = '';
    }
    const resultElement = document.querySelector('.ai-generation-result');
    if (resultElement) {
      resultElement.style.display = 'none';
    }
    const statusElement = document.querySelector('.ai-generation-status');
    if (statusElement) {
      statusElement.style.display = 'none';
    }
  });
}

// Expose functions to global scope
if (typeof window !== 'undefined') {
  window.initSettingsModal = initSettingsModal;
  window.showSettingsModal = showSettingsModal;
  window.closeSettingsModal = closeSettingsModal;
  window.saveSettings = saveSettings;
  window.resetSettings = resetSettings;
  window.loadSettings = loadSettings;
  window.updateLinkedModelsUI = updateLinkedModelsUI;
  window.getInstructionForFeature = getInstructionForFeature;
  window.getInstructionContent = getInstructionContent;
  window.showCustomInstructionModal = showCustomInstructionModal;
  window.saveCustomInstructionFromForm = saveCustomInstructionFromForm;
  window.toggleInstructionCreationMode = toggleInstructionCreationMode;
  window.generateInstructionWithAI = generateInstructionWithAI;
  window.regenerateInstruction = regenerateInstruction;
  window.initCustomInstructionModal = initCustomInstructionModal;
  window.initInstructionsTabInterface = initInstructionsTabInterface;
  window.handleInstructionChange = function (e) {
    const instructionTypeRadios = document.querySelectorAll(
      'input[name="instructionType"]'
    );
    const type = e.target.id.replace('InstructionBtn', '').toLowerCase();
    if (type && instructionTypeRadios) {
      const ui = initInstructionsUI();
      if (ui && ui.handleInstructionChange) {
        ui.handleInstructionChange(e);
      }
    }
  };
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
    initInstructionsUI,
    getInstructionForFeature,
    getInstructionContent,
    showCustomInstructionModal,
    saveCustomInstructionFromForm,
    addCustomInstruction,
    updateCustomInstruction,
    deleteCustomInstruction,
    importCustomInstructions,
    toggleInstructionCreationMode,
    generateInstructionWithAI,
    regenerateInstruction,
    initCustomInstructionModal,
    initInstructionsTabInterface,
  };
}
