# Jellyfin 2 Samsung

<p align="center">
  <img src="https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/blob/master/.github/jellyfin-tizen-logo.svg" width="250" height="250" />
</p>

<div align="center">
  <p>A simple tool for installing <strong>Jellyfin</strong> on your <strong>Samsung Smart TV</strong> — quickly and effortlessly.</p>
  
  <img src="https://img.shields.io/badge/✅_Supports_all_Tizen_versions-blue?style=for-the-badge" /> 
  <img src="https://img.shields.io/badge/😤_Tired_of_the_certificate_error%3F-You're_in_the_right_place!-brightgreen?style=for-the-badge" />
  <a href="https://discord.gg/DvnbKuuT"><img src="https://img.shields.io/badge/Ask%20it%20on%20Discord-7289DA?style=for-the-badge&logo=discord&logoColor=white" /></a>

  
  Huge thanks to <a href="https://github.com/jeppevinkel/jellyfin-tizen-builds">jeppevinkel</a> for providing the Jellyfin Tizen `.wgt` builds — super helpful and much appreciated!
</div>

---

## 🔥 [Check out the official page for Jellyfin 2 Samsung](https://patrickst1991.github.io/Samsung-Jellyfin-Installer/)

## 📦 Current Versions

<!-- versions:start -->

| Channel    | Version                                | Notes                                      |
|------------|----------------------------------------|--------------------------------------------|
| **Stable** | [v1.7.5](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.7.5)           | Recommended for most users                 |
| **Beta**   | [N/A](#)               | Includes new features, may be less stable  |

<!-- versions:end -->

---

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/M4M71JOT9R)

## 🚀 How It Works

### 1. Launch the Tool  
![Start screen](https://github.com/user-attachments/assets/d7666ba7-a1a4-4b31-8035-a8a3eef3b7e2)  
The tool automatically scans your local network for compatible Samsung Smart TVs.

---

### 2. Select a Release  
<img width="536" height="368" alt="image" src="https://github.com/user-attachments/assets/083b92d3-b09a-440f-95a1-a1fe30c83576" />

Choose the desired Jellyfin release.

---

### 3. Pick a Version  
<img width="536" height="368" alt="image" src="https://github.com/user-attachments/assets/68b2f5cf-9e2d-4963-856f-bc45995ed8fe" />

Select the specific Jellyfin version you’d like to install.

---

### 4. Select Your TV  
<img width="536" height="368" alt="image" src="https://github.com/user-attachments/assets/720545a4-45b7-438f-9b07-a6fec1d67ea5" />

The tool lists all detected Samsung TVs. You can also manually enter an IP if your TV isn’t found.

![Device_not_listed](https://github.com/user-attachments/assets/d9272aad-562a-4485-b52f-885652cd720b)  

---

### 5. Sit Back and Watch the Magic ✨  
<img width="536" height="368" alt="image" src="https://github.com/user-attachments/assets/56d77990-d6c8-47ba-8d8b-2dfe57d658c5" />

Once started, the installer takes care of everything else automatically.

---

### ⚠️ Special Notes for Tizen 7+

<img src="https://github.com/user-attachments/assets/b32a5873-a9d5-4f1e-9266-69f33961917f" alt="Tizen Email" width="400">
<img src="https://github.com/user-attachments/assets/9ad45a0a-f091-4eb6-94e8-eb0f381816d2" alt="Tizen Password" width="400">

Tizen 7+ requires a **Samsung account login** during the install. This step is necessary for generating and exchanging the security certificates used for app installation.

---

## ⚙️ Settings

### Language
<img width="536" height="443" alt="image" src="https://github.com/user-attachments/assets/68bd8a74-785e-4edb-9075-73991c42ac39" />

Select your preferred language.

### Certificate
<img width="536" height="443" alt="image" src="https://github.com/user-attachments/assets/252e9dfb-fbd9-41df-a5ac-a67d51201ab4" />

Choose an existing certificate or let the tool generate a new one automatically.

### Advanced Options
<img width="536" height="443" alt="image" src="https://github.com/user-attachments/assets/c297f7e3-19a9-44c6-81c1-1c53116fa1b3" />

- **Custom WGT:** Upload your own `.wgt` file(s) (randomizes the package name to allow side-by-side installs).  
- **Remember IPs:** Save a manually entered IP when your device isn’t found via scan (one IP at a time).  
- **Remove Old Jellyfin:** Attempts to uninstall previous versions before installation (not supported on all TVs).  
- **Force Samsung Login:** Force the tool to login in order to forcefully create a new certificate.  
- **Right-to-left Reading:** Languages that are right-to-left need to have the IP inverted in order for Tizen Studio to work (192.168.1.2 -> 2.1.168.192).  
- **Jellyfin Config:** Lets you set your Jellyfin App configuration in advance, so once the app is installed, you won’t need to configure it on the TV.  

## 📝 Jellyfin Config
<img width="510" height="283" alt="image" src="https://github.com/user-attachments/assets/8296671f-2fbb-4d7a-b1a9-d2db0228b48e" />

Update Mode consists of:  

| Type | Requirements |
|------|--------------|
| None | - |
| Server Settings | Server IP and port |
| Browser Settings | Server IP and port, API Key and Jellyfin user selection |
| User Settings | Server IP and port, API Key and Jellyfin user selection |
| Server & Browser Settings | Server IP and port, API Key and Jellyfin user selection |
| Server & Users Settings | Server IP and port, API Key and Jellyfin user selection |
| Browser & User Settings | Server IP and port, API Key and Jellyfin user selection |
| All Settings | Server IP and port, API Key and Jellyfin user selection |

### Server Settings
<img width="483" height="219" alt="image" src="https://github.com/user-attachments/assets/f6e6a72f-35e4-4027-9a9d-f94632a851cd" />

This lets you set the Jellyfin server IP address in the config file, so the app doesn't have to search for the server.  
- Fill in the Address information; Server IP and Port.

### Browser Settings
<img width="482" height="510" alt="image" src="https://github.com/user-attachments/assets/25e8052d17cb" />

**Requirements: API Key and Jellyfin User selection**  
This lets you set the browser-specific information for the chosen user(s) like Theme selection, Skip Intro etc. Jellyfin saves this information in your browser.

### User Settings
<img width="484" height="268" alt="image" src="https://github.com/user-attachments/assets/ba09248f-a7e1-4021-a719-8f1cc7a63676" />

**Requirements: API Key and Jellyfin User selection**  
This lets you set all the Jellyfin user specifics for the chosen user(s) like Auto Login, Subtitle Mode etc. Jellyfin saves this on its server.

---

## ✅ Requirements

Before getting started, ensure you have the following:

- A **Samsung Tizen Smart TV** with **Developer Mode enabled**  
- **Tizen Web CLI** installed  
- **Certificate Manager**  
- **Microsoft Edge WebView2 Runtime**  
- A valid **[Samsung Account](https://account.samsung.com/iam/signup)** (required for Tizen 7+)  

> ℹ️ Don’t worry—the installer checks for missing dependencies and guides you through installation if needed.

---

## 🛠️ Support, Feedback & Wiki

Need help or want to report a bug?  
👉 [Open an issue](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  
📖 [Check the wiki](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/wiki)  

Got an idea for improvement?  
💡 Share it on the [Discussions board](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/discussions)  
or [submit a feature request](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  

We welcome all contributions and feedback to improve the experience for everyone!
