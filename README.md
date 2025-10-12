# MustMail

![.NET](https://img.shields.io/badge/.NET-5C2D91)
![Language](https://img.shields.io/github/languages/top/niklasfink/MustMail)
![Release](https://img.shields.io/github/v/release/niklasfink/MustMail)

A simple SMTP server that receives emails and sends them using Microsoft Graph API with OAuth authentication.

> **Note**: This is a fork of [bxdavies/MustMail](https://github.com/bxdavies/MustMail) with enhancements for multi-sender support.

## About

MustMail bridges the gap between legacy applications that only support basic SMTP and Microsoft 365's modern OAuth-based email delivery. Instead of configuring OAuth in each application, MustMail acts as a local SMTP relay that handles authentication via Microsoft Graph API.

**How it works:**  
Application sends email → MustMail receives via SMTP → MustMail sends via Microsoft Graph → Recipient receives email

## Prerequisites

- Microsoft 365 Tenant
- Admin access to grant `Mail.Send` and `User.Read.All` API permissions
- Valid mailboxes in the tenant for each address in `AllowedSenders`

## Azure App Registration

1. Navigate to [App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Click **New Registration**, enter a name, leave defaults
3. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Application permissions**
4. Add `Mail.Send` and `User.Read.All`, then click **Add permissions**
5. Click **Grant admin consent for [Tenant Name]** and confirm
6. Go to **Certificates & secrets** → **Client secrets** → **New client secret**
7. Set expiration (recommended: 24 months), copy the secret value immediately
8. Note the **Client ID** and **Tenant ID** from the Overview tab

> [!TIP]
> Set a calendar reminder to renew the client secret before expiration.

## Installation

> [!CAUTION]
> Only bind to `localhost` - MustMail has no authentication or encryption!

### Docker Compose (Recommended)

```yml
version: "3"

services:
  mustmail:
    image: ghcr.io/bxdavies/mustmail
    container_name: mustmail
    environment:
      - Smtp__Host=localhost
      - Smtp__Port=9025
      - Graph__TenantId=YOUR_TENANT_ID
      - Graph__ClientId=YOUR_CLIENT_ID
      - Graph__ClientSecret=YOUR_CLIENT_SECRET
      - AllowedSenders__0=servers@example.com
      - AllowedSenders__1=notifications@example.com
      - LogLevel=Warning
    restart: unless-stopped
```

### Docker Run

```bash
docker run -d --name mustmail \
  -e Smtp__Host=localhost \
  -e Smtp__Port=9025 \
  -e Graph__TenantId=YOUR_TENANT_ID \
  -e Graph__ClientId=YOUR_CLIENT_ID \
  -e Graph__ClientSecret=YOUR_CLIENT_SECRET \
  -e AllowedSenders__0=servers@example.com \
  -e AllowedSenders__1=notifications@example.com \
  -e LogLevel=Warning \
  ghcr.io/bxdavies/mustmail
```

### Binary Installation

**Configuration file** (`appsettings.json`):
```json
{
  "Graph": {
    "ClientId": "YOUR_CLIENT_ID",
    "TenantId": "YOUR_TENANT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET"
  },
  "LogLevel": "Information",
  "AllowedSenders": [
    "servers@example.com",
    "notifications@example.com"
  ],
  "Smtp": {
    "Host": "localhost",
    "Port": 9025
  }
}
```

**Windows:**
1. Download the latest release from [Releases](https://github.com/bxdavies/MustMail/releases/latest)
2. Extract to `C:\MustMail`
3. Create `appsettings.json` in the same directory (see above)
4. Run the executable
5. Use Task Scheduler to run at startup (import `MustMail.xml`)

**Linux:**
1. Download and extract to `/opt/MustMail`
2. Create `appsettings.json` (see above)
3. Make executable: `sudo chmod +x /opt/MustMail/MustMail`
4. Create systemd service at `/etc/systemd/system/mustmail.service`:
```ini
[Unit]
Description=MustMail SMTP to Graph Relay
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
WorkingDirectory=/opt/MustMail
ExecStart=/opt/MustMail/MustMail

[Install]
WantedBy=multi-user.target
```
5. Enable: `sudo systemctl enable mustmail.service && sudo systemctl start mustmail.service`

## Usage

## Usage

Configure your application's SMTP settings:

```
SMTP_HOST=localhost
SMTP_PORT=9025
SMTP_FROM_EMAIL=servers@example.com
SMTP_SECURE=false
```

> **Docker Networks**: If MustMail runs in Docker and your app is in another container on the same network, use the container name (e.g., `mustmail`) instead of `localhost`.

### Multi-Sender Support

MustMail can relay emails from multiple sender addresses, making it ideal for environments with several applications or services.

**How it works:**

1. **Configure allowed senders** - List all valid sender addresses in `AllowedSenders`
2. **MustMail validates** - When an email arrives via SMTP, MustMail checks the "From" header
3. **Dynamic routing** - If the "From" address is in `AllowedSenders`, the email is sent using that mailbox via Graph API
4. **Rejection** - Emails from unlisted senders are rejected

**Example:**

```json
"AllowedSenders": [
  "servers@example.com",       // Server notifications
  "notifications@example.com", // User notifications  
  "alerts@example.com"         // Monitoring alerts
]
```

With this configuration:
- Your monitoring tool sends from `alerts@example.com`
- Your web app sends from `notifications@example.com`
- Your server sends from `servers@example.com`

Each email is delivered using the appropriate Microsoft 365 mailbox automatically.

**Requirements for each sender:**
- Must be a valid mailbox in your Microsoft 365 tenant
- Must be listed in `AllowedSenders`
- Microsoft Graph API permissions apply to all senders

### Testing

Use an SMTP test tool like [SMTPtool](https://github.com/georgjf/SMTPtool) or test with Docker:

```bash
docker run --rm -ti chko/swaks \
  --to recipient@example.com \
  --from servers@example.com \
  --server localhost \
  --port 9025 \
  --header "Subject: Test Email"
```

Adjust `--network` if using Docker networks, or change `--server` to the container name.

## Configuration Reference

**Environment Variables** (for Docker):
```bash
Smtp__Host=localhost
Smtp__Port=9025
Graph__TenantId=YOUR_TENANT_ID
Graph__ClientId=YOUR_CLIENT_ID
Graph__ClientSecret=YOUR_CLIENT_SECRET
AllowedSenders__0=servers@example.com
AllowedSenders__1=notifications@example.com
LogLevel=Warning
```

**appsettings.json** (for binary):
```json
{
  "Graph": {
    "ClientId": "YOUR_CLIENT_ID",
    "TenantId": "YOUR_TENANT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET"
  },
  "LogLevel": "Information",
  "AllowedSenders": [
    "servers@example.com",
    "notifications@example.com"
  ],
  "Smtp": {
    "Host": "localhost",
    "Port": 9025
  }
}
```

**Log Levels**: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` ([Serilog docs](https://github.com/serilog/serilog/wiki/Configuration-Basics#minimum-level))

## Contributing

Contributions are welcome! Please fork the repo and create a pull request, or open an issue for bugs and feature requests.

## Acknowledgments

- **[bxdavies](https://github.com/bxdavies)** - Original MustMail creator
- **[Cain O'Sullivan](https://github.com/cosullivan)** - [SmtpServer](https://github.com/cosullivan/SmtpServer) library
- **[Georg Felgitsch](https://github.com/georgjf)** - [SMTPtool](https://github.com/georgjf/SMTPtool) for testing

## License

AGPL-3.0 License. See `LICENSE.txt` for details.

## Configuration Reference

### Multi-Sender Support

MustMail supports multiple sender addresses through the `AllowedSenders` configuration. This allows different applications or services to send emails using their own "From" addresses, while all messages are processed through the same MustMail instance.

**How it works:**
1. Configure multiple email addresses in the `AllowedSenders` list (e.g., `servers@example.com`, `notifications@example.com`, `alerts@example.com`)
2. Each address must be a valid mailbox in your Microsoft 365 tenant
3. When MustMail receives an email via SMTP, it extracts the "From" address from the email headers
4. If the "From" address matches one of the addresses in `AllowedSenders`, MustMail sends the email via Microsoft Graph using that sender's mailbox
5. If the "From" address is NOT in the `AllowedSenders` list, the email is rejected with a "Mailbox Unavailable" error

**Example scenario:**
```
AllowedSenders:
  - servers@example.com      (for server notifications)
  - notifications@example.com (for user notifications)
  - alerts@example.com       (for monitoring alerts)
```

Your monitoring tool can send alerts from `alerts@example.com`, while your web app sends user notifications from `notifications@example.com`. MustMail will automatically use the correct sender mailbox based on the "From" header in each incoming SMTP message.

**Important**: Every sender address must:
- Be configured in the `AllowedSenders` list
- Be a valid user mailbox in your Microsoft 365 tenant
- Have the Microsoft Graph API permissions (`Mail.Send`) granted to the Azure app registration

> [!TIP]
> Test your setup with a simple SMTP test tool to make sure everything's working before going live.ub/v/release/bxdavies/MustMail">
    <img src="https://qlty.sh/gh/bxdavies/projects/MustMail/maintainability.png">
</div>
<br />
<div align="center">
  <a href="https://github.com/bxdavies/MustMail">
    <img src=".images/logo.png" alt="Logo" width="200" height="200">
  </a>
</div>
<h2 align="center"> MustMail </h2>
<p align="center">
    MustMail is a small SMTP server that receives emails and then sends them using Microsoft Graph.
    <br>
    <a href="https://github.com/bxdavies/MustMail/issues">Report Bug</a>
    ·
    <a href="https://github.com/bxdavies/MustMail/discussions">Request Feature</a>
    ·
    <a href="https://github.com/bxdavies/MustMail/discussions">Get Support</a>
</p>

## About
As of January 2023, Microsoft disabled basic authentication for Exchange, requiring users to switch to OAuth. While basic authentication for SMTP AUTH is still available if you have "Security defaults" turned off, this application offers a solution if you want "Security Defaults" enabled and the application you are trying to send emails from does not support SMTP AUTH using OAuth, and you cannot or do not want to use direct send.

This application acts as a simple SMTP server with no authentication or encryption. When it receives an email, it sends it using the Microsoft Graph API authenticated via OAuth.

Application sends email → MustMail receives the email → MustMail sends the email using Microsoft Graph → Recipient receives the email

## Prerequisites
- A Microsoft 365 Tenant.
- A user with appropriate admin roles (Global Administrator, Privileged Role Administrator, Application Administrator, or Cloud Application Administrator) who can grant Application `Mail.Send` and `User.Read.All` API permissions.
- The email addresses used in AllowedSenders must be valid addresses within the tenant.

## Azure App Creation
1. Go to the ['App registrations' section in Azure](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade).
2. Click 'New Registration'.
3. Enter a name and leave everything else as default.
4. Navigate to 'API permissions' and click 'Add a permission'.
5. Choose 'Microsoft Graph', then select 'Application permissions', then find `Mail.Send` and tick it. Do the same for `User.Read.All`. Finally, press 'Add permissions'.
6. Grant admin consent by clicking 'Grant admin consent for Tenant Name' (where Tenant Name is the name of your Microsoft 365 tenant). Hit 'Yes' at confirmation.
7. Navigate to 'Certificates & secrets', choose the 'Client secrets' tab, then click 'New client secret', enter a description and set expiry to 24 months or a custom value.
    > [!TIP]
    > Set a reminder in your calendar now for 24 months' time to renew and update this secret.
8. Copy the secret value and make note of it.
    > [!IMPORTANT]
    > The secret value is only displayed once.
9. The Client ID and Tenant ID can be found in the overview tab.

## Installation
> [!CAUTION]
> Do not set SMTP Host to anything other than `localhost` because the server does not have authentication or encryption!

### Binary

#### Windows
1. Download the binary release `MustMail-v0.0.0-win-x64.zip` (where 0.0.0 is the latest version) from [here](https://github.com/bxdavies/MustMail/releases/latest).
2. Extract the zip file to `C:\MustMail`.
3. Create an `appsettings.json` file in the same directory as the executable.
4. Add the following to the file and fill in ClientId, TenantId, ClientSecret, and AllowedSenders between the empty double quotes:
```json
{
  "Graph": {
    "ClientId": "",
    "TenantId": "",
    "ClientSecret": ""
  },
  "LogLevel": "Information",
  "AllowedSenders": [
    "servers@example.com",
    "notifications@example.com"
  ],
  "Smtp": {
    "Host": "localhost",
    "Port": 9025
  }
}
```
5. Launch the executable, approve the firewall prompt, and test with [SMTP Test Tool](https://github.com/georgjf/SMTPtool).
6. Open 'Task Scheduler'.
7. Click 'Import Task' and select the `MustMail.xml` file located at `C:\MustMail`.
8. Click 'Change User or Group'.
9. Enter your username (your folder name in C:\Users) in the textbox and click 'Check Names'. It should find your username, then click 'Ok'.
10. Click 'OK'.
11. Right-click on the task and press 'Run'.
12. Test again with [SMTP Test Tool](https://github.com/georgjf/SMTPtool).

#### Linux
1. Download the binary release `MustMail-v0.0.0-linux-x64.tar.gz` (replace `0.0.0` with the latest version) from [here](https://github.com/bxdavies/MustMail/releases/latest).
2. Extract the archive to `/opt/MustMail` (you may need sudo):
```bash
sudo mkdir -p /opt/MustMail
sudo tar -xzf MustMail-v0.0.0-linux-x64.tar.gz -C /opt/MustMail
```
3. Change to the installation directory: `cd /opt/MustMail`.
4. Create the appsettings.json file alongside the executable: `sudo nano appsettings.json`.
5. Add the following to the file and fill in ClientId, TenantId, ClientSecret, and AllowedSenders between the empty double quotes:
```json
{
  "Graph": {
    "ClientId": "",
    "TenantId": "",
    "ClientSecret": ""
  },
  "LogLevel": "Information",
  "AllowedSenders": [
    "servers@example.com",
    "notifications@example.com"
  ],
  "Smtp": {
    "Host": "localhost",
    "Port": 9025
  }
}
```
6. Make the executable runnable: `sudo chmod +x MustMail`.
7. Create a systemd service to run MustMail once at startup after the network is online:
   a) Create the service file: `sudo nano /etc/systemd/system/mustmail.service`
   b) Add this:
```
[Unit]
Description=Run MustMail once after network is online
After=network-online.target
Wants=network-online.target

[Service]
Type=oneshot
WorkingDirectory=/opt/MustMail
ExecStart=/opt/MustMail/MustMail

[Install]
WantedBy=multi-user.target
```
8. Reload systemd to apply changes: `sudo systemctl daemon-reload`.
9. Enable the service so it runs once at boot: `sudo systemctl enable mustmail.service`.
10. Reboot your system or start the service manually to test: `sudo systemctl start mustmail.service`.
11. Verify it ran successfully: `sudo systemctl status mustmail.service`
12. Test with telnet following [these instructions from StackOverflow](https://stackoverflow.com/a/11988455)

### Docker image

Run MustMail in a container with these simple steps.

#### Docker run
Start MustMail listening on localhost port 9025. Override any environment variable below to match your setup.
```bash
docker run --name MustMail
-e Smtp__Host="localhost" \
-e Smtp__Port=9025 \
-e Graph__TenantId="" \
-e Graph__ClientId="" \
-e Graph__ClientSecret="" \
-e AllowedSenders__0="servers@example.com" \
-e AllowedSenders__1="notifications@example.com" \
-e LogLevel="Warning" \
-d ghcr.io/bxdavies/mustmail
```

#### Docker compose
Use Docker Compose for easier management. Fill in your values and you’re ready to go.
```yml
version: "3"

services:
  mustmail:
    image: ghcr.io/bxdavies/mustmail
    container_name: mustmail
    environment:
      - Smtp__Host="localhost"
      - Smtp__Port=9025
      - Graph__TenantId=""
      - Graph__ClientId=""
      - Graph__ClientSecret=""
      - AllowedSenders__0="servers@example.com"
      - AllowedSenders__1="notifications@example.com"
      - LogLevel="Warning"
    restart: unless-stopped
```

## Usage

Set up your application to send emails through MustMail by configuring the SMTP settings like this:

```
SMTP_HOST=localhost
SMTP_PORT=9025
SMTP_FROM_EMAIL=servers@example.com
SMTP_SECURE=false
```

Names for these settings might vary depending on your app—check its documentation if you’re not sure.

If you’re running MustMail in Docker and your app is in another container on the same network, use the container name (e.g. `mustmail`) instead of `localhost` for `SMTP_HOST`.

> [!TIP]
> Test your setup with a simple SMTP test tool to make sure everything’s working before going live.

### Quick SMTP testing with Docker

If you want to quickly test MustMail, you can spin up a test SMTP client container. For example, try [hko/swaks](https://hub.docker.com/r/chko/swaks):

```bash
docker run --network docker_default --rm -ti chko/swaks --to name@example.com --from servers@example.com --server mustmail --port 9025 --header "Subject:first contact"
```

- `--to` sets the recipient email address (change this to where you want the test email delivered).
- `--from` should match the address you’ve configured as your sender in MustMail.
- `--server` is the hostname or container name for MustMail (use `mustmail` if you’re on the same Docker network).
- `--port` sets the SMTP port (default is 9025).
- `--header "Subject:first contact"` sets the subject line for your test email.
- If you’re using Docker Compose or a custom network, update `--network docker_default` to match your setup.

## Environment variables & appsettings.json reference

Log levels can be found on the Serilog wiki [here](https://github.com/serilog/serilog/wiki/Configuration-Basics#minimum-level).

### Environment variables
```
Smtp__Host="localhost"
Smtp__Port=9025
Graph__TenantId=""
Graph__ClientId=""
Graph__ClientSecret=""
AllowedSenders__0="servers@example.com"
AllowedSenders__1="notifications@example.com"
LogLevel="Warning"
```

Note: For environment variables, arrays are configured using indexed notation (e.g., `AllowedSenders__0`, `AllowedSenders__1`, etc.).

### appsettings.json
```json
{
  "Graph": {
    "ClientId": "",
    "TenantId": "",
    "ClientSecret": ""
  },
  "LogLevel": "Information",
  "AllowedSenders": [
    "servers@example.com",
    "notifications@example.com"
  ],
  "Smtp": {
    "Host": "localhost",
    "Port": 9025
  }
}
```

## Contributing
Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

If you have a suggestion that would make this better, please fork the repo and create a pull request. You can also simply open an issue with the tag "enhancement".
Don't forget to give the project a star! Thanks again!

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## Acknowledgments

[Cain O'Sullivan](https://github.com/cosullivan) - [SmtpServer](https://github.com/cosullivan/SmtpServer) developer (an SMTP Server component written in C#)

[balanv](https://stackoverflow.com/users/651016/balanv) - [StackOverflow Answer- How to check if SMTP is working from commandline (Linux) ](https://stackoverflow.com/a/11988455)

[Georg Felgitsch](https://github.com/georgjf) - [SMTPtool](https://github.com/georgjf/SMTPtool) A slim .NET based UI tool to communicate with SMTP servers and to quickly compose, mail and remail messages 

## License

Distributed under the AGPL-3.0 License. See `LICENSE.txt` for more information.
