/**
 * chat.js
 * Handles all chat and prompt interaction functionality for the MagicPrompt extension.
 */

'use strict';

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
                }
            },
            // Instructions
            instructions: {
                chat: '',
                vision: '',
                caption: '',
                prompt: ''
            }
        }
    };
}

// Initialize ChatHandler only if it doesn't exist
if (!window.ChatHandler) {
    window.ChatHandler = class ChatHandler {
        constructor() {
            // Chat state
            this.messages = [];
            this.isTyping = false;
            this.lastMessageId = 0;
            // Bind methods
            this.submitInput = this.submitInput.bind(this);
            this.appendMessage = this.appendMessage.bind(this);
            this.clearMessage = this.clearMessage.bind(this);
            this.regenerateMessage = this.regenerateMessage.bind(this);
            this.handleKeyPress = this.handleKeyPress.bind(this);
            this.handleModeChange = this.handleModeChange.bind(this);
        }

        initialize() {
            try {
                this.setupUIElements();
                this.bindEvents();
            } catch (error) {
                console.error('Failed to initialize chat handler:', error);
                showError('Failed to initialize chat interface');
            }
        }

        setupUIElements() {
            this.elements = {
                // Main containers
                chatSection: getRequiredElementById('chat_section'),
                chatMessages: getRequiredElementById('chat_messages'),
                // Input elements
                chatInput: getRequiredElementById('chat_llm_textarea'),
                submitButton: getRequiredElementById('submit_button'),
                // UI elements
                loadingIndicator: getRequiredElementById('loading_indicator'),
                settingsButton: getRequiredElementById('settings_button'),
                messageTemplate: getRequiredElementById('message_template'),
                // Mode controls
                unloadModelsToggle: getRequiredElementById('unload_models_toggle'),
                chatModeRadio: getRequiredElementById('chat_mode'),
                visionModeRadio: getRequiredElementById('vision_mode'),
                promptModeRadio: getRequiredElementById('prompt_mode'),
                // Input controls
                inputControls: document.querySelector('.input-controls'),
                chatInputWrapper: document.querySelector('.chat-input-wrapper')
            };
            // Set initial placeholder based on mode
            this.updateInputPlaceholder();
        }

        bindEvents() {
            const {
                chatInput,
                submitButton,
                settingsButton,
                unloadModelsToggle,
                chatModeRadio,
                visionModeRadio,
                promptModeRadio
            } = this.elements;
            // Input events
            chatInput.addEventListener('keydown', this.handleKeyPress);
            chatInput.addEventListener('input', () => {
                this.adjustInputHeight();
            });
            // Button events
            submitButton.addEventListener('click', this.submitInput);
            settingsButton.addEventListener('click', () => {
                const modal = document.getElementById('settingsModal');
                const bootstrapModal = new bootstrap.Modal(modal);
                initSettingsModal(); // Initialize settings before showing modal
                bootstrapModal.show();
            });
            // Mode events
            chatModeRadio.addEventListener('change', this.handleModeChange);
            visionModeRadio.addEventListener('change', this.handleModeChange);
            promptModeRadio.addEventListener('change', this.handleModeChange);
            unloadModelsToggle.addEventListener('change', () => {
                const currentBackend = MP.settings.backend;
                if (currentBackend === 'ollama') {
                    // Use consistent capitalization
                    MP.settings.backends[currentBackend].unloadModel = unloadModelsToggle.checked;
                    console.log(`Setting unloadModel for ${currentBackend} to ${unloadModelsToggle.checked}`);
                    saveSettings();
                }
            });
            // Set unload models toggle
            if (unloadModelsToggle) {
                const currentBackend = MP.settings.backend;
                unloadModelsToggle.checked = MP.settings.backends[currentBackend]?.unloadModel || false;
                console.log(`Initial toggle state set to: ${unloadModelsToggle.checked} for backend ${currentBackend}`);
            }
            // Message action delegates
            this.elements.chatMessages.addEventListener('click', (e) => {
                const actionButton = e.target.closest('.action-button');
                if (!actionButton) return;
                const messageEl = actionButton.closest('.chat-message');
                if (!messageEl) return;
                const action = actionButton.dataset.tooltip;
                const messageId = messageEl.dataset.messageId;
                switch (action) {
                    case 'Clear Message':
                        this.clearMessage(messageId);
                        break;
                    case 'Use as Prompt':
                        this.useAsPrompt(messageId);
                        break;
                    case 'Regenerate Image':
                        this.regenerateMessage(messageId);
                        break;
                }
            });
        }

        handleModeChange() {
            const { visionModeRadio, promptModeRadio } = this.elements;
            this.updateInputPlaceholder();
            // Update any UI elements that depend on mode
            document.dispatchEvent(new Event('modeChanged'));
        }

        updateInputPlaceholder() {
            const { chatInput, visionModeRadio, promptModeRadio } = this.elements;
            
            if (visionModeRadio.checked) {
                chatInput.placeholder = "Vision Mode: Ask questions about the uploaded image or request detailed analysis...";
            } else if (promptModeRadio.checked) {
                chatInput.placeholder = "Prompt Mode: Describe your image idea and get an enhanced, detailed prompt for generation...";
            } else {
                chatInput.placeholder = "Chat Mode: Have a conversation, ask questions, or get assistance with any topic...";
            }
        }

        handleKeyPress(e) {
            // Submit on Enter (without shift)
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.submitInput();
            }
        }
        
        async submitInput() {
            const { chatInput, loadingIndicator, visionModeRadio } = this.elements;
            const input = chatInput.value.trim();
            if (!input || this.isTyping) return;
            // Check if we're in vision mode and there's no image
            const previewImage = document.getElementById('preview_image');
            if (visionModeRadio.checked && (!previewImage || !previewImage.src)) {
                this.appendMessage('system', 'Please upload an image first to use vision mode.');
                return;
            }
            try {
                // Show user message
                this.appendMessage('user', input);
                // Clear input
                chatInput.value = '';
                this.adjustInputHeight();
                // Create request payload
                const promptMode = document.getElementById('prompt_mode')?.checked;
                const visionMode = document.getElementById('vision_mode')?.checked;
                const currentImage = previewImage?.src ? previewImage.src.split(',')[1] : null;
                // Determine the feature based on mode
                const featureAction = visionMode ? 'vision-mode' : (promptMode ? 'prompt-mode' : 'chat-mode');
                const payload = MP.RequestBuilder.createRequestPayload(
                    input,
                    currentImage, // Get image if in vision mode
                    featureAction
                );
                // Show typing indicator
                loadingIndicator.style.display = 'block';
                this.isTyping = true;
                // Make API request
                const response = await MP.APIClient.makeRequest(payload);
                if (response.success && response.response) {
                    this.appendMessage('assistant', response.response);
                } else {
                    throw new Error(response.error || 'Failed to get response');
                }
            }
            catch (error) {
                console.error('Chat submission error:', error);
                this.appendMessage('system', `Error: ${error.message}`);
            }
            finally {
                // Hide typing indicator
                loadingIndicator.style.display = 'none';
                this.isTyping = false;
            }
        }

        async useAsPrompt(messageId) {
            const message = this.findMessage(messageId);
            if (!message || message.role !== 'assistant') {
                return;
            }
            try {
                // Navigate to generate tab if available
                const generateTab = document.getElementById('generatetabclickable')
                    || document.getElementById('text2imagetabbutton');
                if (generateTab) {
                    generateTab.click();
                    generateTab.dispatchEvent(new Event('change', { bubbles: true }));
                }
                // Set prompt text
                const promptBox = document.getElementById('alt_prompt_textbox');
                if (promptBox) {
                    promptBox.value = message.content;
                    triggerChangeFor(promptBox);
                    promptBox.focus();
                    promptBox.setSelectionRange(0, promptBox.value.length);
                }
            }
            catch (error) {
                console.error('Send to prompt error:', error);
                showError(`Failed to send to prompt: ${error.message}`);
            }
        }

        appendMessage(role, content) {
            const messageId = ++this.lastMessageId;
            const message = { id: messageId, role, content, timestamp: new Date() };
            this.messages.push(message);
            // Clone template
            const template = this.elements.messageTemplate.content.cloneNode(true);
            const messageDiv = template.querySelector('.chat-message');
            messageDiv.dataset.messageId = messageId;
            messageDiv.classList.add(`${role}-message`);
            // Set avatar
            const avatar = messageDiv.querySelector('.avatar');
            avatar.textContent = role === 'user' ? '👤' : role === 'assistant' ? '🤖' : '⚠️';
            // Set content
            const contentDiv = messageDiv.querySelector('.message-content');
            contentDiv.textContent = content;
            // Hide actions for non-assistant messages
            if (role !== 'assistant') {
                messageDiv.querySelector('.message-actions')?.remove();
            }
            this.elements.chatMessages.appendChild(messageDiv);
            this.scrollToBottom();
        }

        clearMessage(messageId) {
            const messageIndex = this.messages.findIndex(m => m.id === parseInt(messageId));
            if (messageIndex === -1) return;
            const message = this.messages[messageIndex];
            // If assistant message, also remove preceding user message
            if (message.role === 'assistant' && messageIndex > 0) {
                const userMessage = this.messages[messageIndex - 1];
                if (userMessage.role === 'user') {
                    this.elements.chatMessages
                        .querySelector(`.chat-message[data-message-id="${userMessage.id}"]`)
                        ?.remove();
                    this.messages.splice(messageIndex - 1, 1);
                }
            }
            // Remove message
            this.messages.splice(messageIndex, 1);
            this.elements.chatMessages
                .querySelector(`.chat-message[data-message-id="${messageId}"]`)
                ?.remove();
        }

        async regenerateMessage(messageId) {
            const message = this.findMessage(messageId);
            if (!message) return;
            // Find associated user message
            const userMessage = this.findPrecedingUserMessage(messageId);
            if (!userMessage) {
                showError('Cannot find original message to regenerate');
                return;
            }
            // Remove only the assistant's message
            const assistantMessageElement = this.elements.chatMessages
                .querySelector(`.chat-message[data-message-id="${messageId}"]`);
            assistantMessageElement?.remove();
            // Remove the assistant message from the messages array
            const messageIndex = this.messages.findIndex(m => m.id === parseInt(messageId));
            if (messageIndex !== -1) {
                this.messages.splice(messageIndex, 1);
            }

            try {
                const checkedButton = document.querySelector('input[name="chat_mode"]:checked');
                const chatMode = checkedButton
                    ? document.querySelector(`label[for="${checkedButton.id}"]`).textContent.toLowerCase() : null;
                const payload = MP.RequestBuilder.createRequestPayload(
                    userMessage.content,
                    null,
                    chatMode
                );
                // Show typing indicator animation
                this.elements.loadingIndicator.style.display = 'block';
                this.isTyping = true;
                const response = await MP.APIClient.makeRequest(payload);
                if (response.success && response.response) {
                    this.appendMessage('assistant', response.response);
                } else {
                    throw new Error(response.error || 'Failed to regenerate response');
                }
            }
            catch (error) {
                console.error('Message regeneration error:', error);
                this.appendMessage('system', `Error: ${error.message}`);
            }
            finally {
                this.elements.loadingIndicator.style.display = 'none';
                this.isTyping = false;
            }
        }

        findMessage(messageId) {
            return this.messages.find(m => m.id === parseInt(messageId));
        }

        findPrecedingUserMessage(messageId) {
            const messageIndex = this.messages.findIndex(m => m.id === parseInt(messageId));
            if (messageIndex <= 0) return null;
            const userMessage = this.messages[messageIndex - 1];
            return userMessage.role === 'user' ? userMessage : null;
        }

        adjustInputHeight() {
            const input = this.elements.chatInput;
            input.style.height = 'auto';
            input.style.height = `${Math.min(200, Math.max(60, input.scrollHeight))}px`;
        }

        scrollToBottom() {
            const { chatMessages } = this.elements;
            chatMessages.scrollTop = chatMessages.scrollHeight;
        }
    }
}

// Create and initialize chat handler
const chatHandler = new window.ChatHandler();
document.addEventListener('DOMContentLoaded', () => {
    chatHandler.initialize();
});

// Export for use in other modules
window.chatHandler = chatHandler;

// Global functions for message actions
window.clearMessage = function(button) {
    const messageDiv = button.closest('.chat-message');
    const messageId = messageDiv?.dataset.messageId;
    if (messageId) {
        chatHandler.clearMessage(messageId);
    }
};

window.sendToPrompt = function(button) {
    const messageDiv = button.closest('.chat-message');
    const messageId = messageDiv?.dataset.messageId;
    if (messageId) {
        chatHandler.useAsPrompt(messageId);
    }
};

window.regenerate = function(button) {
    const messageDiv = button.closest('.chat-message');
    const messageId = messageDiv?.dataset.messageId;
    if (messageId) {
        chatHandler.regenerateMessage(messageId);
    }
};

/**
 * Handler for chat submission
 */
async function handleChatResponse(response) {
    try {
        if (!response.success) {
            stopTypingAnimation();
            if (response.error && (response.error.includes("API Key not found") || response.error.includes("API key"))) {
                showChatError(response.error);
            } else {
                showError(response.error || 'Unknown error');
            }
            aiTyping = false;
            return;
        }
    } catch (error) {
        console.error('Error handling chat response:', error);
    }
}