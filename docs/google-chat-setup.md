# Google Chat setup for Codions

This guide describes how to connect the Codions Chat Adapter to Google Chat so users can create jobs from Chat.

## Prerequisites

- A Google Cloud project
- Codions.Api running (e.g. on port 5005)
- Codions.ChatAdapter running (e.g. on port 5006)
- A **public HTTPS URL** for the adapter (required by Google Chat). For local development, use [ngrok](https://ngrok.com/) or similar.

## 1. Google Cloud project

1. Open [Google Cloud Console](https://console.cloud.google.com/).
2. Create a new project or select an existing one.
3. Enable the **Google Chat API**:
   - Go to **APIs & Services** > **Library**.
   - Search for "Google Chat API" and enable it.

## 2. Create a Chat app

1. Go to [Google Chat API configuration](https://console.cloud.google.com/apis/api/chat.googleapis.com/hangouts-chat) (or **APIs & Services** > **Enabled APIs** > **Google Chat API** > **Configure**).
2. Under **Configuration**, click **Add new app** or edit an existing app.
3. **App name**: e.g. "Codions".
4. **Avatar**: optional image URL.
5. **Description**: e.g. "Create Codions coding jobs from Chat."
6. **Connection settings**:
   - Select **HTTP** (not Apps Script).
   - **URL**: your adapter’s public webhook URL, e.g.:
     - Production: `https://your-adapter.azurewebsites.net/webhook`
     - Local dev (ngrok): `https://xxxx-xx-xx-xx-xx.ngrok.io/webhook`
7. **Permissions**: ensure the app can receive and send messages (default for HTTP apps).
8. **Visibility**: choose who can use the app (e.g. specific org or "Anyone" for testing).
9. Save. If a **Verification token** is shown, copy it and set it in the adapter config (see below).

## 3. Verification token (optional)

If Google provides a verification token in the Chat app configuration:

1. Add it to the adapter’s configuration, e.g. in `appsettings.json` or environment:
   ```json
   "GoogleChat": {
     "VerificationToken": "your-token-here"
   }
   ```
2. The adapter will reject requests that don’t include this token, so only Google can trigger the webhook.

If no token is shown (modern Chat apps may use bearer tokens instead), leave `VerificationToken` empty.

## 4. Local development with ngrok

Google Chat can only call HTTPS URLs. To test locally:

1. Install [ngrok](https://ngrok.com/download).
2. Start Codions.Api (e.g. `dotnet run` in `src/Codions.Api`).
3. Start Codions.ChatAdapter (e.g. `dotnet run` in `src/Codions.ChatAdapter`).
4. Expose the adapter:
   ```bash
   ngrok http 5006
   ```
5. Copy the HTTPS URL (e.g. `https://abc123.ngrok.io`) and set the Chat app’s URL to `https://abc123.ngrok.io/webhook`.
6. In a Chat space or DM, add the Codions app and send a message in the required format.

## 5. Message format

Users send a message in this format:

```
repo: owner/repo-name
title: Fix login bug
description: User cannot log in when 2FA is enabled.
```

- **repo**: GitHub repo as `owner/repo-name` (e.g. `acme/web-api`). Only GitHub is supported in v1.
- **title**: Required. Short task title.
- **description**: Optional. Can span multiple lines after the first `description:` line.

If the message is empty or invalid, the bot replies with usage help.

## 6. Configuration summary

| Setting | Description |
|--------|-------------|
| `CodionsApi:BaseUrl` | Base URL of Codions API (e.g. `http://localhost:5005`). Required. |
| `GoogleChat:VerificationToken` | Optional. If set, incoming webhook requests must include this token. |

## 7. References

- [Google Chat API](https://developers.google.com/workspace/chat)
- [Build a Google Chat app as a webhook](https://developers.google.com/workspace/chat/quickstart/webhooks)
- [Event types and message formats](https://developers.google.com/workspace/chat/api/guides/message-formats/events)
- [Verify requests from Google Chat](https://developers.google.com/workspace/chat/verify-requests-from-chat) (bearer token verification for custom servers)
