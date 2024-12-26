/**
 * vision.js
 * Handles all vision and image-related functionality for the MagicPrompt extension.
 */

'use strict';

// Initialize VisionTab only if it doesn't exist
if (!window.VisionTab) {
    window.VisionTab = class VisionTab {
        constructor() {
            this.elements = {};
            this.setupElements();
            this.setupEventListeners();
        }

        setupElements() {
            this.elements = {
                dropZone: document.getElementById('image_upload_area'),
                imagePreview: document.getElementById('preview_image'),
                uploadButton: document.getElementById('upload_image_button'),
                imageInput: document.getElementById('image_input'),
                captionBtn: document.getElementById('caption_btn'),
                editBtn: document.getElementById('edit_btn'),
                useInitBtn: document.getElementById('use_init_btn'),
                useAsPromptBtn: document.getElementById('use_as_prompt_btn'),
                clearBtn: document.getElementById('clear_image_btn'),
                captionContent: document.querySelector('.caption-content'),
                captionContainer: document.querySelector('.caption-container'),
                loadingSpinner: document.querySelector('.typing-animation'),
                previewContainer: document.getElementById('image_preview_container'),
                uploadPlaceholder: document.querySelector('.upload-placeholder'),
                visionActions: document.querySelector('.vision-actions')
            };
        }

        setupEventListeners() {
            // Setup drag and drop
            this.elements.dropZone.addEventListener('dragover', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.elements.dropZone.classList.add('dragover');
            });
            this.elements.dropZone.addEventListener('dragleave', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.elements.dropZone.classList.remove('dragover');
            });
            this.elements.dropZone.addEventListener('drop', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.elements.dropZone.classList.remove('dragover');
                if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                    this.handleFile(e.dataTransfer.files[0]);
                }
            });
            // Setup upload button
            this.elements.uploadButton.addEventListener('click', () => {
                this.elements.imageInput.click();
            });
            // Setup file input
            this.elements.imageInput.addEventListener('change', (e) => {
                if (e.target.files && e.target.files.length > 0) {
                    this.handleFile(e.target.files[0]);
                }
            });
            // Setup other buttons
            this.elements.captionBtn.addEventListener('click', this.generateCaption.bind(this));
            this.elements.useInitBtn.addEventListener('click', this.useAsInit.bind(this));
            this.elements.useAsPromptBtn.addEventListener('click', this.useAsPrompt.bind(this));
            this.elements.editBtn.addEventListener('click', this.editImage.bind(this));
            this.elements.clearBtn.addEventListener('click', this.clearImage.bind(this));
            // Global paste event
            document.addEventListener('paste', (e) => {
                const items = (e.clipboardData || e.originalEvent.clipboardData).items;
                for (const item of items) {
                    if (item.type.indexOf('image') === 0) {
                        const file = item.getAsFile();
                        this.handleFile(file);
                        break;
                    }
                }
            });
        }

        handleFile(file) {
            if (!file || !file.type.startsWith('image/')) {
                showError('Please upload a valid image file');
                return;
            }

            const reader = new FileReader();
            reader.onload = (e) => {
                try {
                    // Use SwarmUI's parseMetadata function if available
                    if (typeof window.parseMetadata === 'function') {
                        window.parseMetadata(e.target.result, (data, metadata) => {
                            this.setImage(data, metadata);
                        });
                    } else {
                        this.setImage(e.target.result, null);
                    }
                } catch (error) {
                    this.setImage(e.target.result, null);
                }
            };
            reader.readAsDataURL(file);
        }

        setImage(dataUrl, metadata) {
            // Set image in our preview
            this.elements.imagePreview.src = dataUrl;
            this.elements.imagePreview.style.display = 'block';
            this.elements.previewContainer.style.display = 'block';
            this.elements.uploadPlaceholder.style.display = 'none';
            this.elements.visionActions.style.display = 'flex';
            // Set image in SwarmUI's system
            if (typeof window.setCurrentImage === 'function') {
                window.setCurrentImage(dataUrl, '', '', false, false, true, false);
            }
            // Auto generate caption if enabled
            const autoCaptionCheckbox = document.getElementById('auto_caption_checkbox');
            if (autoCaptionCheckbox?.checked) {
                this.generateCaption();
            }
        }

        generateCaption = async () => {
            if (!this.elements.imagePreview.src) {
                showError('No image to caption');
                return;
            }
            try {
                const { captionContainer, captionContent, loadingSpinner } = this.elements;
                loadingSpinner.classList.add('active');
                captionContent.style.display = 'none';
                const payload = MP.RequestBuilder.createRequestPayload(
                    "Generate a detailed caption for this image",
                    this.elements.imagePreview.src.split(',')[1],
                    "caption"
                );
                const response = await MP.APIClient.makeRequest(payload);
                loadingSpinner.classList.remove('active');
                if (response.success && response.response) {
                    captionContent.style.display = 'block';
                    captionContent.textContent = response.response;
                } else {
                    throw new Error(response.error || 'Failed to generate caption');
                }
            } catch (error) {
                console.error('Caption generation error:', error);
                this.elements.loadingSpinner.classList.remove('active');
                showError(`Failed to generate caption: ${error.message}`);
            }
        }

        useAsInit = () => {
            const buttons = document.querySelectorAll('.current-image-buttons button');
            const initButton = Array.from(buttons).find(btn => btn.textContent.includes('Use As Init'));
            if (initButton) {
                initButton.click();
                const generateTab = document.getElementById('generatetabclickable')
                    || document.getElementById('text2imagetabbutton');
                if (generateTab) {
                    generateTab.click();
                    generateTab.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
        }

        editImage = () => {
            const buttons = document.querySelectorAll('.current-image-buttons button');
            const editButton = Array.from(buttons).find(btn => btn.textContent.includes('Edit Image'));
            if (editButton) {
                editButton.click();
                const generateTab = document.getElementById('generatetabclickable')
                    || document.getElementById('text2imagetabbutton');
                if (generateTab) {
                    generateTab.click();
                    generateTab.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
        }

        useAsPrompt() {
            if (!this.elements.captionContent?.textContent) {
                showError('No caption available to send to prompt');
                return;
            }
            try {
                const captionText = this.elements.captionContent.textContent;
                const generateTab = document.getElementById('generatetabclickable')
                    || document.getElementById('text2imagetabbutton');
                if (generateTab) {
                    generateTab.click();
                    generateTab.dispatchEvent(new Event('change', { bubbles: true }));
                }
                const promptBox = document.getElementById('alt_prompt_textbox');
                if (promptBox) {
                    promptBox.value = captionText;
                    triggerChangeFor(promptBox);
                    promptBox.focus();
                    promptBox.setSelectionRange(0, promptBox.value.length);
                }
            } catch (error) {
                console.error('Send to prompt error:', error);
                showError(`Failed to send to prompt: ${error.message}`);
            }
        }

        clearImage = () => {
            this.elements.imagePreview.src = '';
            this.elements.imagePreview.style.display = 'none';
            this.elements.previewContainer.style.display = 'none';
            this.elements.uploadPlaceholder.style.display = 'block';
            this.elements.visionActions.style.display = 'none';
            this.elements.captionContent.textContent = '';
            this.elements.imageInput.value = '';
        }
    }

    // Create and initialize vision tab
    const visionTab = new window.VisionTab();

    // Export for use in other modules
    window.visionTab = visionTab;
}