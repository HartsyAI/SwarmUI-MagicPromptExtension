/**
 * vision.js
 * Handles all vision and image-related functionality for the MagicPrompt extension.
 */

'use strict';

// Initialize VisionHandler only if it doesn't exist
if (!window.VisionHandler) {
    window.VisionHandler = class VisionHandler {
        constructor() {
            // Image state
            this.currentImage = null;
            this.currentPreview = null;
            this.currentMediaType = null;
            this.metadata = {};

            // Config
            this.config = {
                maxSize: 5 * 1024 * 1024,
                allowedTypes: ['image/jpeg', 'image/png', 'image/webp'],
                maxDimension: 2048
            };

            // Bind methods
            this.handleDragOver = this.handleDragOver.bind(this);
            this.handleDragLeave = this.handleDragLeave.bind(this);
            this.handleDrop = this.handleDrop.bind(this);
            this.handleFileSelect = this.handleFileSelect.bind(this);
            this.generateCaption = this.generateCaption.bind(this);
            this.useAsInit = this.useAsInit.bind(this);
            this.useAsPrompt = this.useAsPrompt.bind(this);
            this.editImage = this.editImage.bind(this);
            this.clearImage = this.clearImage.bind(this);
        }

        initialize() {
            try {
                this.setupUIElements();
                this.bindEvents();
                console.log('Vision handler initialized');
            } catch (error) {
                console.error('Failed to initialize vision handler:', error);
                showError('Failed to initialize vision interface');
            }
        }

        setupUIElements() {
            this.elements = {
                // Main containers
                visionSection: getRequiredElementById('vision_section'),
                uploadArea: getRequiredElementById('image_upload_area'),
                uploadPlaceholder: document.querySelector('.upload-placeholder'),

                // Image elements
                imageInput: getRequiredElementById('image_input'),
                previewContainer: getRequiredElementById('image_preview_container'),
                previewImage: getRequiredElementById('preview_image'),
                uploadButton: getRequiredElementById('upload_image_button'),

                // Info section elements
                infoSection: document.querySelector('.vision-info-section'),
                visionActions: document.querySelector('.vision-actions'),
                captionContainer: document.querySelector('.caption-container'),
                captionContent: document.querySelector('.caption-content'),
                typingAnimation: document.querySelector('.typing-animation'),
                metadataContainer: document.querySelector('.metadata-container'),

                // Action buttons
                captionBtn: getRequiredElementById('caption_btn'),
                useInitBtn: getRequiredElementById('use_init_btn'),
                useAsPromptBtn: getRequiredElementById('use_as_prompt_btn'),
                editBtn: getRequiredElementById('edit_btn'),
                clearBtn: getRequiredElementById('clear_image_btn')
            };
        }

        bindEvents() {
            const {
                uploadArea,
                imageInput,
                uploadButton,
                captionBtn,
                useInitBtn,
                useAsPromptBtn,
                editBtn,
                clearBtn
            } = this.elements;

            // Upload area events
            uploadArea.addEventListener('dragover', this.handleDragOver);
            uploadArea.addEventListener('dragleave', this.handleDragLeave);
            uploadArea.addEventListener('drop', this.handleDrop);

            // File input events
            imageInput.addEventListener('change', this.handleFileSelect);
            uploadButton.addEventListener('click', () => imageInput.click());

            // Action button events
            captionBtn.addEventListener('click', this.generateCaption);
            useInitBtn.addEventListener('click', this.useAsInit);
            useAsPromptBtn.addEventListener('click', this.useAsPrompt)
            editBtn.addEventListener('click', this.editImage);
            clearBtn.addEventListener('click', this.clearImage);

            // Global paste event
            document.addEventListener('paste', this.handleImagePaste.bind(this));
        }

        handleDragOver(e) {
            e.preventDefault();
            e.stopPropagation();
            this.elements.uploadArea.classList.add('dragover');
        }

        handleDragLeave(e) {
            e.preventDefault();
            e.stopPropagation();
            this.elements.uploadArea.classList.remove('dragover');
        }

        handleDrop(e) {
            e.preventDefault();
            e.stopPropagation();

            this.elements.uploadArea.classList.remove('dragover');
            const files = e.dataTransfer.files;

            if (files?.length) {
                this.handleImageUpload(files[0]);
            }
        }

        handleFileSelect(e) {
            const files = e.target.files;
            if (files?.length) {
                this.handleImageUpload(files[0]);
            }
        }

        handleImagePaste(e) {
            const items = (e.clipboardData || e.originalEvent.clipboardData).items;
            for (let item of items) {
                if (item.type.startsWith('image/')) {
                    const file = item.getAsFile();
                    this.handleImageUpload(file);
                    break;
                }
            }
        }

        async handleImageUpload(file) {
            if (!this.validateFile(file)) {
                return;
            }
            try {
                const reader = new FileReader();
                reader.onload = async (e) => {
                    const image = new Image();
                    image.onload = async () => {
                        this.setCurrentImage(
                            e.target.result,
                            file.type,
                            {
                                width: image.naturalWidth,
                                height: image.naturalHeight,
                                size: (file.size / 1024).toFixed(2) + ' KB'
                            }
                        );
                        this.displayImage();
                        // Auto generate caption if enabled
                        const autoCaptionCheckbox = document.getElementById('auto_caption_checkbox');
                        if (autoCaptionCheckbox?.checked) {
                            await this.generateCaption();
                        }
                    };
                    image.src = e.target.result;
                };
                reader.readAsDataURL(file);
            }
            catch (error) {
                console.error('Image upload error:', error);
                showError(`Failed to upload image: ${error.message}`);
            }
        }

        validateFile(file) {
            if (!file) return false;

            if (!this.config.allowedTypes.includes(file.type)) {
                showError('Invalid file type. Please upload a JPG, PNG, or WebP image.');
                return false;
            }

            if (file.size > this.config.maxSize) {
                showError('File too large. Maximum size is 5MB.');
                return false;
            }

            return true;
        }

        setCurrentImage(imageData, mediaType, metadata = {}) {
            console.log('Setting image with media type:', mediaType);
            this.currentImage = imageData.includes('base64,') ?
                imageData.split('base64,')[1] : imageData;
            this.currentMediaType = mediaType;
            this.metadata = metadata;

            // Hide placeholder, show preview
            this.elements.uploadPlaceholder.style.display = 'none';
            this.elements.previewContainer.style.display = 'block';
            this.elements.visionActions.style.display = 'flex';
        }

        displayImage() {
            const { 
                previewContainer,
                previewImage, 
                metadataContainer,
                uploadPlaceholder,
                visionActions
            } = this.elements;
            // Hide placeholder and show preview
            uploadPlaceholder.style.display = 'none';
            previewContainer.style.display = 'flex';
            previewImage.style.display = 'block';
            // Display image
            previewImage.src = `data:${this.currentMediaType};base64,${this.currentImage}`;
            // Show actions
            visionActions.style.display = 'flex';
            // Display metadata
            metadataContainer.innerHTML = `
                <div class="image-metadata">
                    <span>Width: ${this.metadata.width}px</span>
                    <span>Height: ${this.metadata.height}px</span>
                    <span>Size: ${this.metadata.size}</span>
                </div>
            `;
        }

        async generateCaption() {
            if (!this.currentImage) {
                showError('No image to caption');
                return;
            }
            try {
                const { captionContainer, captionContent, typingAnimation } = this.elements;
                typingAnimation.classList.add('active');
                captionContent.style.display = 'none';
                const payload = MP.RequestBuilder.createRequestPayload(
                    "Generate a detailed caption for this image",
                    this.currentImage,
                    "Vision"
                );
                const response = await MP.APIClient.makeRequest(payload);
                typingAnimation.classList.remove('active');
                if (response.success && response.response) {
                    captionContent.style.display = 'block';
                    captionContent.textContent = response.response;
                } else {
                    throw new Error(response.error || 'Failed to generate caption');
                }
            }
            catch (error) {
                console.error('Caption generation error:', error);
                this.elements.typingAnimation.classList.remove('active');
                showError(`Failed to generate caption: ${error.message}`);
            }
        }

        useAsInit() {
            if (!this.currentImage) {
                showError('No image to use');
                return;
            }
            try {
                const initImageParam = document.getElementById('input_initimage');
                if (!initImageParam) {
                    showError('Init image parameter not available');
                    return;
                }
                // Create file from image data
                fetch(`data:${this.currentMediaType};base64,${this.currentImage}`)
                    .then(res => res.blob())
                    .then(blob => {
                        const file = new File([blob], 'init_image.png', { type: this.currentMediaType });
                        const container = new DataTransfer();
                        container.items.add(file);
                        initImageParam.files = container.files;
                        // First toggle the group open
                        if (typeof toggleGroupOpen === 'function') {
                            toggleGroupOpen(initImageParam, true);
                        }
                        // Then set and trigger the content toggle
                        const toggler = document.getElementById('input_group_content_initimage_toggle');
                        if (toggler) {
                            toggler.checked = true;
                            triggerChangeFor(toggler);
                        }
                        // Finally trigger change on the file input
                        triggerChangeFor(initImageParam);
                        // TODO: Add nav to Genpage
                    });
            }
            catch (error) {
                console.error('Use as init error:', error);
                showError(`Failed to set init image: ${error.message}`);
            }
        }

        useAsPrompt() {
            if (!this.elements.captionContent?.textContent) {
                showError('No caption available to send to prompt');
                return;
            }

            try {
                // Get the caption text
                const captionText = this.elements.captionContent.textContent;

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
                    promptBox.value = captionText;
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

        editImage() {
            if (!this.currentImage) {
                showError('No image to edit');
                return;
            }
            try {
                const image = new Image();
                image.onload = () => {
                    window.imageEditor.setBaseImage(image);
                    window.imageEditor.activate();
                };
                image.src = `data:${this.currentMediaType};base64,${this.currentImage}`;
            }
            catch (error) {
                console.error('Failed to open editor:', error);
                showError(`Failed to open editor: ${error.message}`);
            }
        }

        clearImage() {
            // Reset state
            this.currentImage = null;
            this.currentPreview = null;
            this.currentMediaType = null;
            this.metadata = {};
            // Reset UI elements
            const {
                uploadPlaceholder,
                previewContainer,
                previewImage,
                visionActions,
                metadataContainer,
                captionContent,
                imageInput
            } = this.elements;
            // Reset visibility
            uploadPlaceholder.style.display = '';
            previewContainer.style.display = 'none';
            previewImage.src = '';
            previewImage.style.display = 'none';
            visionActions.style.display = 'none';
            // Clear content but preserve structure
            metadataContainer.innerHTML = '';
            if (captionContent) {
                captionContent.textContent = ''; // Clear text
                captionContent.style.display = 'none'; // Hide the content
            }
            // Reset file input
            imageInput.value = '';
        }

        getCurrentImage() {
            return this.currentImage;
        }

        getMetadata() {
            return this.metadata;
        }
    }
}

// Create and initialize vision handler
const visionHandler = new window.VisionHandler();
document.addEventListener('DOMContentLoaded', () => {
    visionHandler.initialize();
});

// Export for use in other modules
window.visionHandler = visionHandler;