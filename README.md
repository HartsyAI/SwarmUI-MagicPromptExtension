# SwarmUI MagicPrompt Extension
===============================

![Kalebbroo LLC](./Images/kalebbroo.png)

## Table of Contents
-----------------

1. [Introduction](#introduction)
2. [Features](#features)
3. [Prerequisites](#prerequisites)
4. [Installation](#installation)
5. [Usage](#usage)
6. [Configuration](#configuration)
7. [Troubleshooting](#troubleshooting)
8. [Changelog](#changelog)
9. [License](#license)
10. [Contributing](#contributing)
11. [Acknowledgments](#acknowledgments)

## Introduction
---------------

The Magic Prompt Extension is a browser extension that provides a simple and intuitive way to generate text prompts for Stable Diffusion models.

## Features
------------

* Generate text prompts for Stable Diffusion models
* Supports multiple models and configurations
* Easy-to-use interface for customizing prompts
* Compatible with other Kalebbroo SwarmUI extensions

## Prerequisites
----------------

Before you install the MagicPrompt Extension, ensure that you have the following prerequisites:

* You need to have SwarmUI installed on your system. If you don't have it installed, you can download it from [here](https://github.com/mcmonkeyprojects/SwarmUI).
* This extension assumes you have a working and setup local LLM API server. If you don't have one installed, you can download Jan AI from [here](https://jan.ai). If you want to use a paid API like OpenAI you can also set that up in Jan AI.

## Installation
--------------

To install the MagicPrompt Extension, follow these steps:

1. Close your SwarmUI instance and navigate to `SwarmUI/src/Extensions` directory and clone the repo there. Open cmd `cd` to the directory above and `git clone ` the repo.
2. Run `update-windows.bat` or `update-linuxmac.sh` to recompile the project.
3. Configue the extension as described in the Configuration section.
4. Restart your SwarmUI instance.

## Configuration
----------------

The MagicPrompt Extension can be used with ay LLM model that is supported by Jan AI. To configure the extension, follow these steps:

1. Open the extension folder in your SwarmUI instance and open the `config.json` file.
2. replace the `LlmEndpoint` with the URL of your LLM API server. and If you want, replace the `Instructions` with your own.				
3. Currently, you need to hardcode the model name in the MagicPromptAPI.cs file. This will change in the future.
4. Save your changes and rebuild the project.

## Usage
--------

1. When you open your SwarmUI instance, you will see a new tab called "Kalebbroo". All the extensions will be under this tab.
![Image description](./Images/Screenshots/kalebbroo_tab.png)
2. Enter your crappy prompt in the box and click submit or hit enter. 
![Image description](./Images/Screenshots/magicprompt_tab.png)
3. It will rewrite the prompt for your review.	
![Image description](./Images/Screenshots/rewritten.png)
4. If you like the prompt, click send to prompt button and it will yeet it to the Generate tab and fill in your prompt box.
![Image description](./Images/Screenshots/generate.png)

## Troubleshooting
-----------------

If you encounter any issues check the common solutions before you open an issue on GitHub.

* Check the logs for any error messages or warnings.
* Ensure that the extension is properly installed and configured. Did you add your API URL to the config.json file?
* If you are using any LLM service other than Jan AI, I cannot guarantee that it will work. You may need to modify the code to work with your service.
* Ask in the SwarmUI Discord server for help. That is one of the places I live.
* If you still have issues, open an issue on GitHub or join my [Dev Discord Server](https://discord.com/invite/5m4Wyu52Ek)

## Changelog
------------

* Version 0.1: Initial release

## License
----------

Kalebbroo Extensions including this one are licensed under the [MIT License](https://opensource.org/licenses/MIT).

## Contributing
---------------

Contributions to the extension are welcome. Please ask before working on anything big. I may already be working on it.

1. Fork the extension's repository on GitHub.
2. Make your changes and commit them to your fork.
3. Open a pull request and wait for a review.

## Acknowledgments
------------------

These extensions would not have been made without the existance of SwarmUI. I would like to thank the developer [mcmonkey](https://github.com/mcmonkey4eva) for being the GOAT he is.

Special thanks to the following people:

* [maedtb](https://github.com/maedtb) and [Jelosus1](https://github.com/gokayfem), Thank you for the support on Discord.  
* [Hartsy AI](https://hartsy.ai) for the daily inspiration. If you work hard, dreams can come true. 
