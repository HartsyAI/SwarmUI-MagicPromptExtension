document.addEventListener("DOMContentLoaded", function ()
{
    const html = `
        <!-- HartsyTab -->
        <li class="nav-item" role="presentation">
            <a class="nav-link translate" data-bs-toggle="tab" href="#hartsytab" id="hartsytabbutton" aria-selected="false" tabindex="-1" role="tab">Hartsy.AI</a>
        </li>
        <div class="tab-pane" id="hartsytab" role="tabpanel">
            <ul class="nav nav-tabs" id="hartsytablist">
            </ul>
        </div>
    `;
    let tempDiv = document.createElement('div');
    tempDiv.innerHTML = html;
    let newTab = tempDiv.querySelector('li');
    let newTabContent = tempDiv.querySelector('.tab-pane');
    // Check if the tab already exists from another extension
    let existingTab = document.getElementById('hartsytabbutton');
    if (!existingTab)
    {
        // Inject the new tab into the tab list
        let tabList = document.getElementById('toptablist');
        tabList.appendChild(newTab);
        // Inject the new tab content into the tab content container
        let tabContentContainer = document.querySelector('.tab-content');
        tabContentContainer.appendChild(newTabContent);
    }
    try
    {
        addMagicPromptTab(); // Add the MagicPrompt tab to the Hartsy tab
    } catch (error)
    {
        console.error("Error adding MagicPrompt tab:", error);
    }
});

async function addMagicPromptTab()
{
    const hartsyTabList = document.getElementById('hartsytablist');
    if (!hartsyTabList)
    {
        console.error('Hartsy tab list element not found');
        return;
    }
    const magicPromptTabButton = document.getElementById('magicprompttabbutton');
    if (!magicPromptTabButton)
    {
        const newTabButton = document.createElement('li');
        newTabButton.className = 'nav-item';
        newTabButton.innerHTML = `
            <a class="nav-link translate" data-bs-toggle="tab" href="#MagicPrompt-Tab" id="magicprompttabbutton" aria-selected="false" tabindex="-1" role="tab">MagicPrompt</a>
        `;
        hartsyTabList.appendChild(newTabButton);
        const hartsyTabContent = document.getElementById('hartsytab');
        if (!hartsyTabContent)
        {
            console.error('Hartsy tab content container not found');
            return;
        }
        const newTabContent = document.createElement('div');
        newTabContent.className = 'tab-pane';
        newTabContent.id = 'MagicPrompt-Tab';
        newTabContent.setAttribute('role', 'tabpanel');
        newTabContent.innerHTML = `
             <div class="card border-secondary mb-3 card-center-container" style="width: 287.5px; float: left; margin-left: 200px; margin-right: 20px; box-sizing: border-box;">
                 <div class="card-header translate">Choose an Installed Model to Load</div>
                 <div class="card-body">
                     <div class="form-group" style="display: flex; align-items: center;">
                         <label for="modelSelect" class="translate" style="margin-right: 10px;">Model:</label>
                         <select id="modelSelect" class="form-control auto-dropdown" style="margin-right: 20px;">
                             <option value="" selected disabled>Loading models...</option>
                         </select>
                     </div>
                 </div>
             </div>
             <div class="card border-secondary mb-3 card-center-container" style="width: 60%; margin-left: 20px; margin-right: 20px;">
                 <div class="card-header translate">MagicPrompt</div>
                 <div class="card-body">
                     <p class="card-text translate">Enter a prompt and let your AI do its magic:</p>
                     <textarea id="chat_llm_textarea" placeholder="A photo of Steve Irwin finding a mcmonkey in the wild, with text in a speech bubble that says aint she a beaute" style="width: 100%; height: 100px;"></textarea>
                     <button id="chat_llm_submit_button" class="basic-button translate" style="margin-top: 10px;">Submit</button>
                     <button id="send_to_prompt_button" class="basic-button translate" style="margin-top: 10px;">Send to Prompt</button>
                     <div id="chat_llm_response" style="margin-top: 20px; white-space: pre-wrap;"></div>
                 </div>
             </div>
         `;
        hartsyTabContent.appendChild(newTabContent);
        try
        {
            await fetchModels();
        }
        catch (error)
        {
            console.error("Error fetching models:", error);
        }
        const submitButton = document.getElementById("chat_llm_submit_button");
        const sendToPromptButton = document.getElementById("send_to_prompt_button");
        const textArea = document.getElementById("chat_llm_textarea");

        submitButton.addEventListener("click", function ()
        {
            submitInput(textArea);
        });
        sendToPromptButton.addEventListener("click", sendToPrompt);
        textArea.addEventListener("keypress", function (event)
        {
            if (event.key === "Enter")
            {
                event.preventDefault();
                submitInput(textArea);
            }
        });
    }
}

async function fetchModels()
{
    const modelSelect = document.getElementById("modelSelect");
    modelSelect.style.color = "inherit";
    try
    {
        const response = await genericRequest(
            'GetAvailableModelsAsync',
            {},
            data =>
            {
                if (data.success)
                {
                    const models = data.models; // Access the models array so it can be looped through
                    modelSelect.innerHTML = ''; // Clear any existing options in the dropdown
                    if (!models || models.length === 0)
                    {
                        showError("No models available");
                    }
                    else
                    {
                        // Populate the dropdown with model names
                        models.forEach(model =>
                        {
                            const option = document.createElement("option");
                            option.value = model.model;
                            option.textContent = model.name;
                            modelSelect.appendChild(option);
                        });
                    }
                }
                else
                {
                    showError(data.error); // Also show error to the user if the API call failed
                    console.error("Call to C# method GetAvailableModelsAsync() failed:", data.error);
                }
            }
        );
    }
    catch (error)
    {
        console.error("Error fetching models:", error);
    }
}

function submitInput(textArea)
{
    try
    {
        const fallbackText = "A futuristic company logo made of giant bold letters that read \"HARTSY.AI\"";
        const inputText = textArea.value || fallbackText;
        if (inputText === fallbackText)
        {
            showError("You did not enter any text... I guess I will just make some crap up..");
        }
        // Get the selected model ID from the dropdown
        const modelSelect = document.getElementById("modelSelect");
        const modelId = modelSelect.value;
        // Call the API with both input text and selected model ID
        makeLLMAPIRequest(inputText, modelId);
        textArea.value = ""; // Clear the text area after submission
        const chatLLMResponseElement = document.getElementById("chat_llm_response");
        chatLLMResponseElement.style.color = "rgba(144, 238, 144, 0.8)"; // Light green color
        chatLLMResponseElement.textContent = `Loading ${modelId} and rewriting the prompt: "${inputText}"...`;
    }
    catch (error)
    {
        console.log("Unexpected error in submitInput function:", error);
        showError(error);
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
            console.log("Prompt textarea not found in Generate tab.");
        }
    }
    catch (error)
    {
        console.log("Unexpected error in sendToPrompt function:", error);
    }
}

function makeLLMAPIRequest(inputText, modelId) {
    genericRequest(
        'PhoneHomeAsync',
        { "inputText": inputText, "modelId": modelId },
        data =>
        {
            if (data.success)
            {
                const chatLLMResponseElement = document.getElementById("chat_llm_response");
                chatLLMResponseElement.style.color = "inherit"; // Reset the color to default. Needed if there was an error message before.
                chatLLMResponseElement.textContent = data.response;
            }
            else
            {
                console.error("Call to C# method PhoneHomeAsync() failed:", data.error);
            }
        }
    );
}

function showError(message)
{
    const responseDiv = document.getElementById("chat_llm_response");
    responseDiv.textContent = `Error: ${message}`;
    responseDiv.style.color = "red";
}
