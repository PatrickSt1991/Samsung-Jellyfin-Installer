# Jellyfin 2 Samsung

<p align="center">
  <img src="https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/blob/master/.github/jellyfin-tizen-logo.svg" width="250" height="250" />
</p>

<div align="center">
  <p>Install <strong>Jellyfin</strong> on your <strong>Samsung Smart TV</strong> — fast, easy, and error-free.</p>

  <img src="https://img.shields.io/badge/✅_Supports_all_Tizen_versions-blue?style=for-the-badge" /> 
  <img src="https://img.shields.io/badge/😤_Tired_of_the_certificate_error%3F-You're_in_the_right_place!-brightgreen?style=for-the-badge" />
  <a href="https://discord.gg/7mga3zh8Cv"><img src="https://img.shields.io/badge/Ask%20it%20on%20Discord-7289DA?style=for-the-badge&logo=discord&logoColor=white" /></a>

  ![OS Support](https://img.shields.io/badge/Windows-Stable-brightgreen?style=for-the-badge)
  ![OS Support](https://img.shields.io/badge/Linux-Stable-brightgreen?style=for-the-badge)
  ![OS Support](https://img.shields.io/badge/macOS-Stable-brightgreen?style=for-the-badge)
  
  <img src="https://img.shields.io/badge/🌐_Available_in-_Danish,_Dutch,_English,_French,_German-blue?style=for-the-badge" />
  <br/>
  🇩🇰 🇳🇱 🇬🇧 🇫🇷 🇩🇪
  <br/><br/>
  Huge thanks to <a href="https://github.com/jeppevinkel/jellyfin-tizen-builds">jeppevinkel</a> for providing the Jellyfin Tizen <code>.wgt</code> builds — super helpful and much appreciated!
</div>

---

## 📦 Current Versions

<!-- versions:start -->

| Channel    | Version                                                             | Notes                        |
|------------|---------------------------------------------------------------------|------------------------------|
| **Stable** | [v1.8.5.7](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.8.5.7)                                        | Recommended for most users   |
| **Beta**   | [v1.8.5.8-beta](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.8.5.8-beta)                                            | Includes new features        |

<!-- versions:end -->

---

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/M4M71JOT9R)

## 🚀 How It Works
---
### ⚠️ Before Running on Unix-Based Systems

Make sure the script is executable.

```sh
chmod +x Jellyfin2Samsung
./Jellyfin2Samsung

```

---
### 1. Launch the Tool  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/a5413292-b6cf-4d6d-bcb5-4fe242f2f4ae" />

The tool automatically scans your local network for compatible Samsung Smart TVs.

---

### 2. Select a Release  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/22870ab8-92e1-40d3-b094-ecbebe312cd6" />

Choose the desired Jellyfin release.

---

### 3. Pick a Version  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/bba53f99-e45b-45ac-896c-d19edfa6fad8" />

Select the specific Jellyfin version you’d like to install.

---

### 4. Select Your TV  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/c2ff42bb-1ba3-46a7-9009-2853666ed96b" />

The tool lists all detected Samsung TVs. You can also manually enter an IP if your TV isn’t found.

<img width="422" height="252" alt="image" src="https://github.com/user-attachments/assets/c38f9fc5-2bd3-4139-bcb3-acbf108b02bd" />

---

### 5. Sit Back and Watch the Magic ✨  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/12e69906-59e5-44f8-a44e-dcb2de46c14f" />

Once started, the installer takes care of everything else automatically.

---

### ⚠️ Special Notes for Tizen 7+

<img src="https://github.com/user-attachments/assets/b32a5873-a9d5-4f1e-9266-69f33961917f" alt="Tizen Email" width="400">
<img src="https://github.com/user-attachments/assets/9ad45a0a-f091-4eb6-94e8-eb0f381816d2" alt="Tizen Password" width="400">

Tizen 7+ requires a **Samsung account login** during the install. This step is necessary for generating and exchanging the security certificates used for app installation.

---

## ⚙️ Settings

### Language
<img width="612" height="612" alt="image" src="https://github.com/user-attachments/assets/f7c03582-965a-4218-b2ff-40a7a0b30f44" />

Select your preferred language.

### Certificate
<img width="612" height="612" alt="image" src="https://github.com/user-attachments/assets/b34fc1ad-6bd0-44c3-a0c7-6cdd256ffa0e" />

Choose an existing certificate or let the tool generate a new one automatically.

### Advanced Options
<img width="612" height="612" alt="image" src="https://github.com/user-attachments/assets/b3deb013-ee16-47c9-ad47-07182c53f4d1" />

- **Custom WGT:** Upload your own `.wgt` file(s) (randomizes the package name to allow side-by-side installs).  
- **Remember IPs:** Save a manually entered IP when your device isn’t found via scan (one IP at a time).  
- **Remove Old Jellyfin:** Attempts to uninstall previous versions before installation (not supported on all TVs).  
- **Force Samsung Login:** Force the tool to login in order to forcefully create a new certificate.  
- **Right-to-left Reading:** Languages that are right-to-left need to have the IP inverted in order for Tizen Studio to work (192.168.1.2 -> 2.1.168.192).  
- **Jellyfin Config:** Lets you set your Jellyfin App configuration in advance, so once the app is installed, you won’t need to configure it on the TV.  

## 📝 Jellyfin Config
<img width="602" height="343" alt="image" src="https://github.com/user-attachments/assets/21c5bc54-95b3-4e26-a07a-ccfd61394088" />

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
<img width="602" height="222" alt="image" src="https://github.com/user-attachments/assets/d385d90c-3fe2-4594-b275-4457d7d60786" />

This lets you set the Jellyfin server IP address in the config file, so the app doesn't have to search for the server.  
- Fill in the Address information; Server IP and Port.

### Browser Settings
<img width="602" height="626" alt="image" src="https://github.com/user-attachments/assets/33730755-9b19-42dd-90c7-bd6461f8ea61" />

**Requirements: API Key and Jellyfin User selection**  
This lets you set the browser-specific information for the chosen user(s) like Theme selection, Skip Intro etc. Jellyfin saves this information in your browser.

### User Settings
<img width="602" height="275" alt="image" src="https://github.com/user-attachments/assets/29891c48-c620-4bc3-a141-16d28ef38d9d" />

**Requirements: API Key and Jellyfin User selection**  
This lets you set all the Jellyfin user specifics for the chosen user(s) like Auto Login, Subtitle Mode etc. Jellyfin saves this on its server.

---

## ✅ Requirements

Before getting started, ensure you have the following:

- A **Samsung Tizen Smart TV** with **Developer Mode enabled**  
- A **Default webbrowser**  
- A valid **[Samsung Account](https://account.samsung.com/iam/signup)** (required for Tizen 7+)  

---

## 🛠️ Support, Feedback & Wiki

Need help or want to report a bug?  
👉 [Open an issue](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  
📖 [Check the wiki](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/wiki)  

Got an idea for improvement?  
💡 Share it on the [Discussions board](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/discussions)  
or [submit a feature request](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  

We welcome all contributions and feedback to improve the experience for everyone!
