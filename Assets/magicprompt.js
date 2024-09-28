document.addEventListener("DOMContentLoaded", function () {
    const maxRetries = 5; // Maximum number of retries
    const retryInterval = 1000; // Retry interval in milliseconds
    let retryCount = 0; // Current retry count

    function checkForMagicPrompt() {
        const magicPrompt = document.getElementById('MagicPrompt');
        if (magicPrompt) {
            // MagicPrompt tab found, add the MagicPrompt tab to the Hartsy tab
            addMagicPromptTab();
        } else {
            retryCount++;
            if (retryCount < maxRetries) {
                console.log(`MagicPrompt tab not found, retrying in ${retryInterval / 1000} seconds...`);
                setTimeout(checkForMagicPrompt, retryInterval);
            } else {
                console.log('MagicPrompt tab not found after' + maxRetries + 'retries. Please ensure HartsyCore extension is installed.');
            }
        }
    }
    checkForMagicPrompt();
});

async function addMagicPromptTab() {
    const magicPromptTabButton = document.getElementById('magicprompttabbutton');
    if (!magicPromptTabButton) {
        console.error('MagicPrompt tab button not found');
        return;
    }
    const magicPromptTabContent = document.getElementById('MagicPrompt');
    if (!magicPromptTabContent) {
        console.error('MagicPrompt tab content not found');
        return;
    }
    magicPromptTabContent.innerHTML = `
        <!-- Choose a Model Section -->
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
                                                </select>
                                                <div class="d-flex align-items-center" style="margin-right: 10px;">
                                                    <input type="checkbox" id="unloadModelCheckbox" />
                                                    <label for="unloadModelCheckbox" class="translate" style="margin-left: 5px;">Unload Model?</label>
                                                </div>
                                            </div>
                                            <div class="form-group translate" style="display: flex; align-items: center;">
                                                <label for="backendUrl" class="translate" style="margin-right: 10px;">Custom Endpoint:</label>
                                                <input type="text" id="backendUrl" class="form-control" placeholder="Enter Backend URL" />
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>
                            <div class="tab-pane fade" id="instructions" role="tabpanel" aria-labelledby="instructions-tab">
                                <textarea id="responseInstructions" class="form-control" rows="3" placeholder="Response instructions for the LLM"></textarea>
                            </div>
                            <div class="tab-pane fade" id="api" role="tabpanel" aria-labelledby="api-key-tab">
                                    <div class="row">
                                        <div class="col-md-6">
                                            <div class="form-group d-flex align-items-center">
                                                <label for="apiBackendSelect" class="translate" style="margin-right: 10px;">Choose API Backend:</label>
                                                <select id="apiBackendSelect" class="nogrow auto-dropdown"  style="width: 60%;">
                                                    <option value="openai">OpenAI</option>
                                                    <option value="claude">Claude</option>
                                                    <!-- Add more backends -->
                                                </select>
                                            </div>
                                        </div>
                                        <div class="col-md-6">
                                            <div class="form-group d-flex align-items-center">
                                                <label for="apiKeyInput" class="translate" style="margin-right: 10px; white-space: nowrap;">API Key:</label>
                                                <textarea id="apiKeyInput" class="styled-textarea" rows="1" style="width: 100%;" placeholder="Enter API key"></textarea>
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
    try {
        await fetchModels();
    } catch (error) {
        console.error("Error fetching models:", error);
        showMessage('error', 'An error occurred while fetching models: ' + error);
    }

    const submitButton = document.getElementById("chat_llm_submit_button");
    const sendToPromptButton = document.getElementById("send_to_prompt_button");
    const regenerateButton = document.getElementById("regenerate");
    const modelSelect = document.getElementById("modelSelect");
    const textArea = document.getElementById("chat_llm_textarea");
    const settingsModal = new bootstrap.Modal(document.getElementById('settingsModal'));
    const saveApiKeyButton = document.getElementById("saveApiKeyButton");

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
}

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

function submitInput(inputText, buttonType)
{
    try
    {
        const fallbackText = "A futuristic company logo made of giant bold letters that read \"HARTSY.AI\"";
        const originalPrompt = document.getElementById("original_prompt");
        const textArea = document.getElementById("chat_llm_textarea");
        // Determine the text to use based on which button was clicked
        let textToUse;
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

function makeLLMAPIRequest(inputText, modelId)
{
    genericRequest('PhoneHomeAsync',
        { "inputText": inputText, "modelId": modelId },
        data =>
        {
            if (data.success)
            {
                const chatLLMResponse = document.getElementById("chat_llm_response");
                chatLLMResponse.style.color = "inherit"; // Reset the color to default. Needed if there was an error message before.
                chatLLMResponse.textContent = data.response;
            }
            else
            {
                console.error("Call to C# method PhoneHomeAsync() failed:", data.error);
                showMessage('error', 'An error occurred while calling PhoneHomeAsync(): ' + data.error);
            }
        }
    );
}

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
