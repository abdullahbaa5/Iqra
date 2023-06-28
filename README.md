# Iqra (And God Said, READ!)

_"Ignite conversations, Spark intelligence."_

## Overview

Iqra is a state-aware chatbot designed specifically for Discord text channels. Currently harnessing the power of OpenAI's Chat Models, Iqra is programmed to interact, respond, and engage in conversations just like a human would. It was designed with the aim of making Discord chats more engaging and interactive.

Test it Out in: https://discord.gg/hxyT7DcrJe (if its online)

## Logic

As Iqra is semi-programmed to be self-aware, this means that there are progammatic ways included that we check whether Iqra is supposed to reply or not while also including an AI Validation step.
What does that really mean?

First we check the most recent message if Iqra is mentioned by its name, "Iqra" or "iqra" or mentioned by its tag "<@iqra_id>", or the message is a reply to Iqra's messsage. (As this means Iqra is relevant to the most recent conversation going on).
^ this is done in the function (private async Task<bool> isIqraMentionedDirectly)

However, in case that fails and places where Iqra is made relevant in the recent message by referencing to her as third part, we ask another prompted ai to figure out if iqra is relevant in the conversation.
^ this is done in the function (private async Task<bool> isIqraMentionedIndirectly) and the context is available in file IsMentionedSystemContext.txt

Why not just let the main Iqra's System Context take care of this?
Well after alot of revisions I have not been able to come to a prompt (with gpt 3.5 turbo as that is the cheapest), for the model to understand not to reply to itself, or a message it has already replied to, or reply to a conversation it is not a part of.

However, this is still not perfect as sometimes the context in "IsMentionedSystemContext.txt" fails as well when Iqra is mentioned in messages such as replying to Iqra with something like "Oh awesome. I like python 3.10 as well.". But this can be improved upon and you may use gpt 4 for this which will result in better accuracy but will be more costly.

## Installation

1. Clone the Iqra repository from GitHub.
2. Open the .sln file in Visual Studio 2022.
3. Build the IqraChatBot project
4. Edit the appsettings.json with your tokens and settings.
5. Run the Bot

## Usage

Once installed and running.
Iqra is accessible in the text channel you have specified in appsettings.json.
Iqra can be called upon by tagging her or by taking its name "Iqra" or "iqra".

## Context

You can modify the personality of Iqra in the file ChatSystemContext.txt under (## Description about Iqra).
- Modifying its personality should not affect the overall functionality (depends on what you plan to add there).

You can modify the conversational rules of Iqra in the file ChatSystemContext.txt under (## Conversational Rules).
- I would suggest not to change them alot unless you think a rule can be improved upon.
- Wording matters when it comes to context writing as "allowed", "should", or "must" all end with a different functionality.

Output rules for generating JSON format is in the file ChatSystemContext.txt under (## Description about Output).
- I would really not recommend editing anything here as this can break the functionality of the program

## Contribution

Your contributions are always welcome! Please feel free to open an issue or create a pull request with your changes.

## License

Iqra is released under the Apache 2.0 license.
