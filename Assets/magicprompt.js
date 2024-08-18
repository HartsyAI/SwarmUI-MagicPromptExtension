document.addEventListener("DOMContentLoaded", function () {
    const html = `
        <!-- KalebbrooTab -->
        <li class="nav-item" role="presentation">
            <a class="nav-link translate" data-bs-toggle="tab" href="#KalebbrooTab" id="kalebbrootabbutton" aria-selected="false" tabindex="-1" role="tab">Kalebbroo</a>
        </li>
        <div class="tab-pane" id="KalebbrooTab" role="tabpanel">
            <ul class="nav nav-tabs" id="kalebbrootablist">
            </ul>
        </div>
    `;
    let tempDiv = document.createElement('div');
    tempDiv.innerHTML = html;
    let newTab = tempDiv.querySelector('li');
    let newTabContent = tempDiv.querySelector('.tab-pane');
    // Check if the tabs already exists from another extension
    let existingTab = document.getElementById('kalebbrootabbutton');
    if (!existingTab) {
        // Inject the new tab into the tab list
        let tabList = document.getElementById('toptablist');
        tabList.appendChild(newTab);
        // Inject the new tab content into the tab content container
        let tabContentContainer = document.querySelector('.tab-content');
        tabContentContainer.appendChild(newTabContent);
    }
    addLLMTab();
});

async function addLLMTab() {
    // Add tab button
    const kalebbrooTabList = document.getElementById('kalebbrootablist');
    if (!kalebbrooTabList) {
        console.error('Kalebbroo tab list element not found');
        return;
    }
    // Check if the MagicPrompt sub-tab already exists
    const magicPromptTabButton = document.getElementById('magicprompttabbutton');
    if (!magicPromptTabButton) {
        const newTabButton = document.createElement('li');
        newTabButton.className = 'nav-item';
        newTabButton.innerHTML = `
            <a class="nav-link translate" data-bs-toggle="tab" href="#MagicPrompt-Tab" id="magicprompttabbutton" aria-selected="false" tabindex="-1" role="tab">MagicPrompt</a>
        `;
        kalebbrooTabList.appendChild(newTabButton);
        // Add tab content
        const kalebbrooTabContent = document.getElementById('KalebbrooTab');
        if (!kalebbrooTabContent) {
            console.error('Kalebbroo tab content container not found');
            return;
        }
        const newTabContent = document.createElement('div');
        newTabContent.className = 'tab-pane';
        newTabContent.id = 'MagicPrompt-Tab';
        newTabContent.setAttribute('role', 'tabpanel');
        newTabContent.innerHTML = `
            <div class="card border-secondary mb-3 card-center-container" style="width: 800px;">
                <div class="card-header translate">MagicPrompt</div>
                <div class="card-body">
                    <p class="card-text translate">Enter a prompt and let your AI do it's magic:</p>
                    <textarea id="chat_llm_textarea" placeholder="A photo of Steve Irwin finding a mcmonkey in the wild, with text in a speech bubble that says aint she a beaute" style="width: 100%; height: 100px; margin-bottom: 10px;"></textarea>
                    <button id="chat_llm_submit_button" class="basic-button translate">Submit</button>
                    <button id="send_to_prompt_button" class="basic-button translate">Send to Prompt</button>
                    <div id="chat_llm_response" style="margin-top: 20px; white-space: pre-wrap;"></div>
                </div>
            </div>
        `;
        kalebbrooTabContent.appendChild(newTabContent);
        const submitButton = document.getElementById("chat_llm_submit_button");
        const sendToPromptButton = document.getElementById("send_to_prompt_button");
        const textArea = document.getElementById("chat_llm_textarea");
        // Add event listeners
        submitButton.addEventListener("click", function () {
            submitInput(textArea);
        });
        sendToPromptButton.addEventListener("click", sendToPrompt);

        textArea.addEventListener("keypress", function (event) {
            if (event.key === "Enter") {
                event.preventDefault(); // Prevent the default action (new line in textarea)
                submitInput(textArea);
            }
        });
    }
}

function submitInput(textArea) {
    try {
        const inputText = textArea.value ?? "Come up with a random prompt for an internet meme about an ai company called Hartsy.AI";
        if (inputText === "Come up with a random prompt for an internet meme") {
            showError("You did not enter any text... I guess I will just make some crap up..");
        }
        makeLLMAPIRequest(inputText);
        textArea.value = ""; // Clear the text area after submission
    } catch (error) {
        console.log("Unexpected error in submitInput function:", error);
        showError(error);
    }
}

function sendToPrompt() {
    try {
        const responseDiv = document.getElementById("chat_llm_response");
        let llmResponse = responseDiv.textContent;
        if (llmResponse.trim() === "") {
            llmResponse = "A futuristic company logo made of giant bold letters that read \"HARTSY.AI\"";
            console.log("No LLM response available, using default prompt.");
        }
        // Switch to the Generate tab and enter the response in the prompt box
        document.getElementById('text2imagetabbutton').click();
        const generatePromptTextarea = document.getElementById("input_prompt");
        if (generatePromptTextarea) {
            generatePromptTextarea.value = llmResponse;
            generatePromptTextarea.dispatchEvent(new Event('input'));
        } else {
            console.log("Prompt textarea not found in Generate tab.");
        }
    } catch (error) {
        console.log("Unexpected error in sendToPrompt function:", error);
    }
}

function makeLLMAPIRequest(inputText) {
    genericRequest(
        'PhoneHomeAsync',
        { "inputText": inputText },
        data => {
            if (data.success) {
                const chatLLMResponseElement = document.getElementById("chat_llm_response");
                chatLLMResponseElement.style.color = "inherit"; // Reset the color to default. Needed if there was an error message before.
                chatLLMResponseElement.textContent = data.response;
            } else {
                console.error("API call failed:", data.error);
            }
        }
    );
}

function showError(message) {
    const responseDiv = document.getElementById("chat_llm_response");
    responseDiv.textContent = `Error: ${message}`;
    responseDiv.style.color = "red";
}
