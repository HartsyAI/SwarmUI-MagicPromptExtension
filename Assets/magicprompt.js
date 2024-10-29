/**
 * Event listener for the DOMContentLoaded event. This function is called when the HTML document has been completely loaded and parsed.
 *
 * It checks for the existence of the Utilities tab and adds the MagicPrompt tab if found.
 * It also creates the MagicPrompt button in the Generate tab.
 *
 * @returns {void}
 */
document.addEventListener("DOMContentLoaded", function () {

    function checkForMagicPrompt() {
        const utilities = document.getElementById('utilities_tab');
        if (utilities) {
            // Utilities tab found, add the MagicPrompt tab
            addMagicPromptTab(utilities);
        } else {
            console.log('Utilities tab not found, something has gone very wrong!');
            return;
        }
    }
    checkForMagicPrompt();
    // Create the MagicPrompt button in the Generate tab
    const generateButton = document.getElementById('alt_generate_button');
    const promptTextArea = document.getElementById('alt_prompt_textbox');
    if (generateButton) {
        const magicPromptContainer = document.createElement('div');
        const magicPromptButton = document.createElement('img');
        magicPromptButton.id = 'magic_prompt_button';
        magicPromptButton.className = 'alt-prompt-buttons magic-prompt-button basic-button translate';
        magicPromptButton.src = 'https://raw.githubusercontent.com/HartsyAI/SwarmUI-MagicPromptExtension/refs/heads/master/Images/mp_button.webp';
        magicPromptButton.alt = 'MagicPrompt';
        magicPromptButton.style.cursor = 'pointer'; // Make the image clickable.
        magicPromptButton.style.border = 'none';
        magicPromptButton.style.background = 'none';

        // Add hover effect
        magicPromptButton.addEventListener('mouseenter', function () {
            magicPromptButton.style.opacity = '0.8'; // Slightly dim the button on hover
            magicPromptButton.style.transform = 'scale(1.05)'; // Slightly enlarge the button on hover
        });
        magicPromptButton.addEventListener('mouseleave', function () {
            magicPromptButton.style.opacity = '1'; // Reset opacity
            magicPromptButton.style.transform = 'scale(1)'; // Reset scale
        });

        magicPromptButton.addEventListener('click', function () {
            const promptText = promptTextArea.value;
            submitInput(promptText, "magic");
        });
        magicPromptContainer.appendChild(magicPromptButton);
        generateButton.parentNode.appendChild(magicPromptContainer);
    }
});

/**
 * Adds the MagicPrompt tab under the Utilities tab.
 *
 * This function creates the MagicPrompt tab and its content, including the model selector, text area, and buttons.
 * It also initializes the tab content and fetches the list of available models.
 *
 * @param {HTMLElement} utilitiesTab - The Utilities tab element.
 * @returns {void}
 */
async function addMagicPromptTab(utilitiesTab) {
    // Add MagicPrompt tab under the Utilities tab
    let tabList = utilitiesTab.querySelector('.nav-tabs');
    let tabContentContainer = utilitiesTab.querySelector('.tab-content');
    //console.log('tabList:', tabList); // debug
    //console.log('tabContentContainer:', tabContentContainer); // debug
    if (!tabList && !tabContentContainer) {
        console.error('Tab content container not found.');
        return;
    }
    // Create the tab link
    const magicPromptTabButton = `
        <li class="nav-item" role="presentation" data-requiredpermission="use_magicprompt">
            <a class="nav-link translate" id="magicprompt_tab" data-bs-toggle="tab" href="#Utilities-MagicPrompt-Tab" role="tab" tabindex="-1" aria-selected="false">MagicPrompt</a>
        </li>`;
    tabList.insertAdjacentHTML('beforeend', magicPromptTabButton);
    // Create the tab content
    const magicPromptTabContent = `
        <!-- Choose a Model Section -->
        <div class="tab-pane" id="Utilities-MagicPrompt-Tab" role="tabpanel">
        <div class="card border-secondary mb-3 card-center-container" style="width: 287.5px; float: left; margin-left: 200px; margin-right: 20px; box-sizing: border-box;">
            <div class="card-header translate">Choose a Model to Load (3B works well)</div>
            <div class="card-body">
                <div class="form-group" style="display: flex; align-items: center;">
                    <label for="modelSelect" class="translate" style="margin-right: 10px;">Models:</label>
                    <select id="modelSelect" class="form-control auto-dropdown" style="margin-right: 20px;">
                        <option value="" selected disabled>Loading models...</option>
                    </select>
                </div>
            </div>
        </div>
        <!-- MagicPrompt Section -->
        <div class="card border-secondary mb-3 card-center-container" style="width: 60%; margin-left: 20px; margin-right: 20px;">
            <div class="card-header translate">MagicPrompt</div>
            <div class="card-body">
                <p class="card-text translate">Enter a prompt and let your AI do its magic:</p>
                <textarea id="chat_llm_textarea" placeholder="A photo of Steve Irwin finding a mcmonkey in the wild, with text in a speech bubble that says aint she a beaute" style="width: 100%; height: 100px;"></textarea>
                <button id="chat_llm_submit_button" class="basic-button translate" style="margin-top: 10px;">Submit</button>
                <button id="send_to_prompt_button" class="basic-button translate" style="margin-top: 10px;">Send to Prompt</button>
                <button id="regenerate" class="basic-button translate" style="margin-top: 10px;">Regenerate</button>
                <button id="settingsButton" class="basic-button translate" style="margin-right: 10px;">Settings</button>
                <div id="original_prompt" style="margin-top: 40px; white-space: pre-wrap;"></div>
                <div id="chat_llm_response" style="margin-top: 20px; white-space: pre-wrap;"></div>
            </div>
        </div>
        <!-- Modal Structure -->
        <div class="modal fade" id="settingsModal" tabindex="-1" aria-labelledby="settingsModalLabel" aria-hidden="true">
            <div class="modal-dialog modal-dialog-centered modal-lg">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title translate" id="settingsModalLabel">Settings</h5>
                        <button type="button" class="btn-close translate" data-bs-dismiss="modal" aria-label="Close"></button>
                    </div>
                    <div class="modal-body">
                        <ul class="nav nav-tabs" id="myTab" role="tablist">
                            <li class="nav-item" role="presentation">
                                <button class="nav-link active" id="backend-tab" data-bs-toggle="tab" data-bs-target="#backend" type="button" role="tab" aria-controls="backend" aria-selected="true">LLM Backend</button>
                            </li>
                            <li class="nav-item" role="presentation">
                                <button class="nav-link" id="instructions-tab" data-bs-toggle="tab" data-bs-target="#instructions" type="button" role="tab" aria-controls="instructions" aria-selected="false">Response Instructions</button>
                            </li>
                            <li class="nav-item" role="presentation">
                                <button class="nav-link" id="api-key-tab" data-bs-toggle="tab" data-bs-target="#api" type="button" role="tab" aria-controls="api" aria-selected="false">API Key (optional)</button>
                            </li>
                        </ul>
                        <div class="tab-content" id="myTabContent">
                            <div class="tab-pane fade show active" id="backend" role="tabpanel" aria-labelledby="backend-tab">
                                <div class="row">
                                    <div class="col-md-12">
                                        <div class="form-group">
                                            <div class="d-flex align-items-center">
                                                <label for="llmBackendSelect" class="translate" style="margin-right: 15px; white-space: nowrap;">Choose LLM Backend:</label>
                                                <select id="llmBackendSelect" class="nogrow auto-dropdown" style="margin-right: 10px; width: auto;">
                                                    <option value="ollama" selected>Ollama</option>
                                                    <option value="openaiapi">OpenAIAPI (local)</option>
                                                    <option value="openai">OpenAI (ChatGPT)</option>
                                                    <option value="anthropic">Anthropic</option>
                                                </select>
                                                <div class="d-flex align-items-center" style="margin-right: 10px;">
                                                    <input type="checkbox" id="unloadModelCheckbox" />
                                                    <label for="unloadModelCheckbox" class="translate" style="margin-left: 5px;">Unload Model?</label>
                                                </div>
                                            </div>
                                            <div class="form-group translate" style="display: flex; align-items: center;">
                                                <label for="backendUrl" class="translate" style="margin-right: 10px;">Custom Endpoint:</label>
                                                <input type="text" id="backendUrl" class="form-control" placeholder="http://localhost:11434" style="background-color: inherit; color: inherit;" />
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                            <div class="tab-pane fade" id="instructions" role="tabpanel" aria-labelledby="instructions-tab">
                                <textarea id="responseInstructions" class="form-control" rows="3" placeholder="Response instructions for the LLM" style="background-color: inherit; color: inherit;"></textarea>
                            </div>
                            <div class="tab-pane fade" id="api" role="tabpanel" aria-labelledby="api-key-tab">
                                    <div class="row">
                                        <div class="col-md-6">
                                            <div class="form-group d-flex align-items-center">
                                                <label for="apiBackendSelect" class="translate" style="margin-right: 10px;">Choose API Backend:</label>
                                                <select id="apiBackendSelect" class="nogrow auto-dropdown"  style="width: 60%;">
                                                    <option value="openai">OpenAI</option>
                                                    <option value="anthropic">Anthropic</option>
                                                    <!-- Add more backends -->
                                                </select>
                                            </div>
                                        </div>
                                        <div class="col-md-6">
                                            <div class="form-group d-flex align-items-center">
                                                <label for="apiKeyInput" class="translate" style="margin-right: 10px; white-space: nowrap;">API Key:</label>
                                                <textarea id="apiKeyInput" class="form-control" rows="1" style="width: 100%; background-color: inherit; color: inherit;" placeholder="Enter API key"></textarea>
                                                <button id="saveApiKeyButton" type="button" class="basic-button translate" style="margin-left: 10px;">Submit</button>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="basic-button translate" data-bs-dismiss="modal">Close</button>
                            <button id="saveChanges" type="button" class="basic-button translate" data-bs-dismiss="modal">Save changes</button>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    `;
    tabContentContainer.insertAdjacentHTML('beforeend', magicPromptTabContent);
    initializeTabContent();
    try {
        await fetchModels();
    } catch (error) {
        console.error("Error fetching models:", error);
        showMessage('error', 'An error occurred while fetching models:' + error);
    }
}

/**
 * Initializes the MagicPrompt tab content by attaching event listeners to the buttons and setting up the model selector.
 *
 * This function is called when the tab content is loaded. It sets up the event listeners for the submit button, 
 * send to prompt button, and regenerate button. It also populates the model selector dropdown list with available models.
 *
 * @returns {void}
 */
function initializeTabContent() {
    // Attach listeners or manipulate tab content
    const submitButton = document.getElementById("chat_llm_submit_button");
    const sendToPromptButton = document.getElementById("send_to_prompt_button");
    const regenerateButton = document.getElementById("regenerate");
    const modelSelect = document.getElementById("modelSelect");
    const textArea = document.getElementById("chat_llm_textarea");
    const settingsModal = new bootstrap.Modal(document.getElementById('settingsModal'));
    const saveApiKeyButton = document.getElementById("saveApiKeyButton");
    if (submitButton) {
        submitButton.addEventListener("click", function () {
            submitInput(textArea.value, "submit");
        });
        regenerateButton.addEventListener("click", function () {
            submitInput(null, "regenerate");
        });
        sendToPromptButton.addEventListener("click", sendToPrompt);
        textArea.addEventListener("keypress", function (event) {
            if (event.key === "Enter") {
                event.preventDefault(); // Prevent the default action, which is to insert a newline
                submitInput(textArea.value, "submit");
            }
        });
        document.getElementById("settingsButton").addEventListener("click", function () {
            settingsModal.show();
        });
        saveApiKeyButton.addEventListener("click", function () {
            const apiKeyInput = document.getElementById("apiKeyInput").value;
            const apiProvider = document.getElementById("apiBackendSelect").value;
            if (apiKeyInput && apiProvider) {
                submitApiKey(apiKeyInput, apiProvider);
            } else {
                showMessage('error', 'Please enter an API key and select a provider.');
            }
        });
        document.getElementById("saveChanges").addEventListener("click", function () {
            saveSettings();
        });
        modelSelect.addEventListener("change", function () {
            let selectedModel = modelSelect.value;
            loadModel(selectedModel);
        });
    } else {
        console.log('Submit button not found.');
    }
}

/**
 * Saves the API key and provider to the backend by making an API request to the 'SaveApiKeyAsync' method.
 *
 * This function is called when the user clicks the "Submit" button in the API Key settings.
 *
 * @param {string} apiKey - The API key to save.
 * @param {string} apiProvider - The API provider to save (e.g., "OpenAI", "Anthropic").
 * @returns {void}
 */
function submitApiKey(apiKey, apiProvider) {
    genericRequest('SaveApiKeyAsync',
        { "apiKey": apiKey, "apiProvider": apiProvider },
        data => {
            if (data.success) {
                console.log("API key saved successfully:", data.response);
                showMessage('success', 'API key saved successfully!');
            } else {
                console.error("Failed to save API key:", data.error);
                showMessage('error', 'Failed to save API key: ' + data.error);
            }
        }
    );
}

/**
 * Loads a selected model from the dropdown list.
 *
 * This function is called when the user selects a model from the dropdown list. It retrieves the selected model ID 
 * and makes an API request to load the model.
 *
 * @param {string} modelId - The ID of the selected model.
 * @returns {void}
 */
function loadModel(modelId) {
    try {
        genericRequest('LoadModelAsync', { "modelId": modelId }, data => {
            if (data.success) {
                console.log("Model loaded successfully:", data.response);
                showMessage('success', 'Model loaded successfully: ' + data.response);
            } else {
                console.error("Failed to load model:", data.error);
                showMessage('error', 'Failed to load model: ' + data.error);
            }
        });
    } catch (error) {
        console.error("Error loading model:", error);
        showMessage('error', 'An error occurred while loading the model. Please try again.');
    }
}

/**
 * Fetches the list of available models from the backend and populates the dropdown list.
 *
 * This function is called when the tab content is loaded. It makes an API request to retrieve the list of available 
 * models and populates the dropdown list with the model names.
 *
 * @returns {void}
 */
async function fetchModels() {
    const modelSelect = document.getElementById("modelSelect");
    modelSelect.style.color = "inherit"; // Reset colors to default
    try {
        const response = await genericRequest('GetModelsAsync', {}, data => {
            if (!data.success) {
                showMessage('error', 'Failed to load configuration or models.');
                console.error("Failed to load configuration or models.");
                throw new Error("Failed to load configuration or models.");
            }
            const llmBackend = data.config.LLMBackend;
            if (!llmBackend || llmBackend.trim() === "") {
                showMessage('warning', 'LLM Backend is not configured. Please click the settings button to set it up.');
                console.error("LLM Backend is not configured. User needs to set up the backend.");
                return false;
            }
            const models = data.models;
            const selectedModel = data.config.Model;
            console.log("Models:", models); // debug
            modelSelect.innerHTML = ''; // Clear any existing options in the dropdown
            if (!models || models.length === 0) {
                console.error("No models available.");
                showMessage('warning', 'No models available. Please check the backend configuration.');
            } else {
                // Populate the dropdown with model names
                models.forEach(model => {
                    const option = document.createElement("option");
                    option.value = model.model;
                    option.textContent = model.name;
                    if (model.model === selectedModel) {
                        option.selected = true;
                    }
                    modelSelect.appendChild(option);
                });
            }
            return true;
        });
        if (!response) {
            return;
        }
    }
    catch (error) {
        console.error("Error fetching models:", error);
        showMessage('error', 'An error occurred while fetching models: ' + error);
    }
}

/**
 * Submits the input text to the LLM API, either by clicking the submit button or by pressing Enter in the text area.
 *
 * This function is called when the user submits the input text. It retrieves the input text from the text area, 
 * determines the text to use based on which button was clicked, and makes an API request to the LLM backend.
 *
 * @param {string} inputText - The input text to submit.
 * @param {string} buttonType - The type of button that was clicked (e.g., "submit", "regenerate").
 * @returns {void}
 */
function submitInput(inputText, buttonType)
{
    try
    {
        const fallbackText = "A futuristic company logo made of giant bold letters that read \"HARTSY.AI\"";
        const originalPrompt = document.getElementById("original_prompt");
        const textArea = document.getElementById("chat_llm_textarea");
        // Determine the text to use based on which button was clicked
        let textToUse;
        if (buttonType === "magic")
        {
            textToUse = inputText || fallbackText;
            originalPrompt.textContent = textToUse;
        }
        if (buttonType === "regenerate")
        {
            textToUse = originalPrompt.textContent || fallbackText;
        }
        else
        {
            textToUse = inputText || fallbackText;
            originalPrompt.textContent = textToUse;
        }
        if (textToUse === fallbackText)
        {
            console.log("No text entered, using default prompt.");
            showMessage('info', 'You did not enter any text... I guess I will just make some crap up..');
        }
        // Get the selected model ID from the dropdown
        const modelSelect = document.getElementById("modelSelect");
        const modelId = modelSelect.value;
        // Call the API with both input text and selected model ID
        makeLLMAPIRequest(textToUse, modelId);
        // Clear the text area only when the submit button is clicked
        if (buttonType === "submit") {
            textArea.value = "";
        }
        const chatLLMResponse = document.getElementById("chat_llm_response");
        chatLLMResponse.style.color = "rgba(144, 238, 144, 0.8)"; // Light green color
        chatLLMResponse.textContent = `Loading ${modelId} and rewriting the prompt: "${textToUse}"...`;
    }
    catch (error)
    {
        console.error("Unexpected error in submitInput function:", error);
        showMessage('error', 'Unexpected error in submitInput function: ' + error);
    }
}

/**
 * Sends the LLM response to the prompt box in the Generate tab.
 *
 * This function is called when the user clicks the "Send to Prompt" button. It retrieves the LLM response from the 
 * response div and sets the value of the prompt box in the Generate tab.
 *
 * @returns {void}
 */
function sendToPrompt()
{
    try
    {
        const fallbackText = "A futuristic company logo made of giant bold letters that read \"HARTSY.AI\"";
        const responseDiv = document.getElementById("chat_llm_response");
        let llmResponse = responseDiv.textContent;
        if (llmResponse.trim() === "")
        {
            llmResponse = fallbackText;
            console.log("No LLM response available, using default prompt.");
        }
        // Switch to the Generate tab and enter the response in the prompt box
        document.getElementById('text2imagetabbutton').click();
        const generatePromptTextarea = document.getElementById("input_prompt");
        if (generatePromptTextarea)
        {
            generatePromptTextarea.value = llmResponse;
            generatePromptTextarea.dispatchEvent(new Event('input'));
        }
        else
        {
            console.error("Prompt textarea not found in Generate tab.");
            showError("error", "Prompt textarea not found in Generate tab.");
        }
    }
    catch (error)
    {
        console.error("Unexpected error in sendToPrompt function:", error);
        showMessage('error', 'Unexpected error in sendToPrompt function: ' + error);

    }
}

/**
 * Makes an API request to the LLM backend with the input text and selected model ID.
 *
 * This function is called when the user submits the input text. It makes an API request to the LLM backend with the 
 * input text and selected model ID, and displays the response in the response div.
 *
 * @param {string} inputText - The input text to submit.
 * @param {string} modelId - The ID of the selected model.
 * @returns {void}
 */
function makeLLMAPIRequest(inputText, modelId)
{
    genericRequest('PhoneHomeAsync',
        { "inputText": inputText, "modelId": modelId },
        data =>
        {
            if (data.success)
            {
                const chatLLMResponse = document.getElementById("chat_llm_response");
                const promptTextArea = document.getElementById('alt_prompt_textbox');
                chatLLMResponse.style.color = "inherit"; // Reset the color to default. Needed if there was an error message before.
                chatLLMResponse.textContent = data.response;
                promptTextArea.value = data.response; // TODO: This should only trigger when we are in the Generate tab and use the MagicPrompt button there
            }
            else
            {
                console.error("Call to C# method PhoneHomeAsync() failed:", data.error);
                showMessage('error', 'An error occurred while calling PhoneHomeAsync(): ' + data.error);
            }
        }
    );
}

/**
 * Saves the current settings, including the selected backend, model unload, and API URL.
 *
 * This function is called when the user clicks the "Save Changes" button. It retrieves the current settings from the 
 * form fields and makes an API request to save the settings.
 *
 * @returns {void}
 */
async function saveSettings() {
    try {
        const llmBackendSelect = document.getElementById("llmBackendSelect");
        const modelUnloadCheckbox = document.getElementById("unloadModelCheckbox");
        const apiUrlInput = document.getElementById("backendUrl");
        const settings = {
            selectedBackend: llmBackendSelect.value,
            modelUnload: modelUnloadCheckbox.checked,
            apiUrl: apiUrlInput.value
        };
        genericRequest('SaveSettingsAsync', settings,
            data => {
                if (data.success) {
                    showMessage('success', 'Settings saved successfully!');
                } else {
                    console.error("Error saving settings:", data.error);
                    showMessage('error', 'An error occurred while saving settings: ' + data.error);
                }
            }
        );
        await fetchModels(); // Refresh the models dropdown
    } catch (error) {
        console.error("Error in saveSettings:", error);
        showMessage('error', 'An error occurred while saving settings: ' + error);
    }
}

/**
 * Displays a message to the user with the specified type and text.
 *
 * This function is used to display error messages, success messages, and other types of messages to the user.
 *
 * @param {string} type - The type of message (e.g., "error", "success", "info", "warning").
 * @param {string} message - The text of the message.
 * @returns {void}
 */
function showMessage(type, message) {
    const responseDiv = document.getElementById("chat_llm_response");
    responseDiv.textContent = message;
    switch (type) {
        case 'error':
            responseDiv.style.color = "red";
            break;
        case 'success':
            responseDiv.style.color = "green";
            break;
        case 'info':
            responseDiv.style.color = "blue";
            break;
        case 'warning':
            responseDiv.style.color = "orange";
            break;
        default:
            responseDiv.style.color = "black";
            break;
    }
}
