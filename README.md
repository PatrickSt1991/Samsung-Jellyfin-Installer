# Jellyfin 2 Samsung

<p align="center">
  <img src="https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/blob/master/.github/jellyfin-tizen-logo.svg" width="250" height="250" />
</p>

<div align="center">
  <p>Install <strong>Jellyfin</strong> on your <strong>Samsung Smart TV</strong> — fast, easy, and error-free.</p>

  <img src="https://img.shields.io/badge/✅_Supports_all_Tizen_TV_versions-blue?style=for-the-badge" /> 
  <img src="https://img.shields.io/badge/✅_Supports_Tizen_Studio_Emulator-blue?style=for-the-badge" />
  <img src="https://img.shields.io/badge/😤_Tired_of_the_certificate_error%3F-You're_in_the_right_place!-brightgreen?style=for-the-badge" />
  <a href="https://discord.gg/7mga3zh8Cv"><img src="https://img.shields.io/badge/Ask%20it%20on%20Discord-7289DA?style=for-the-badge&logo=discord&logoColor=white" /></a>
  <img src="https://img.shields.io/badge/Samsung_TV-Jellyfin_Plugins_Supported-7E57C2?style=for-the-badge&logo=samsung&logoColor=white" />
  <img src="https://img.shields.io/badge/Made_the_Impossible-Jellyfin_Plugins_on_Samsung_TV-black?style=for-the-badge&logo=jellyfin&logoColor=white" />

  ![OS Support](https://img.shields.io/badge/Windows-Stable-brightgreen?style=for-the-badge)
  ![OS Support](https://img.shields.io/badge/Linux-Stable-brightgreen?style=for-the-badge)
  ![OS Support](https://img.shields.io/badge/macOS-Stable-brightgreen?style=for-the-badge)
  
  <img src="https://img.shields.io/badge/🌐_Available_in-_Danish,_Dutch,_English,_French,_German-blue?style=for-the-badge" />
  <br/>
  🇩🇰 🇳🇱 🇬🇧 🇫🇷 🇩🇪
  <br/><br/>
  Massive thanks to <a href="https://github.com/jeppevinkel/jellyfin-tizen-builds">jeppevinkel</a> for providing the Jellyfin Tizen <code>.wgt</code> builds — an absolute lifesaver and hugely appreciated! 🙌
  And a huge shout-out to <a href="https://github.com/Moonfin-Client/Tizen">Hungry__Alpaca</a> for creating the awesome Moonfin fork — truly fantastic work! 🚀💙
</div>

---

## 📦 Current Versions

<!-- versions:start -->

| Channel    | Version                                                             | Notes                        |
|------------|---------------------------------------------------------------------|------------------------------|
| **Stable** | [v1.8.7.0](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.8.7.0)                                        | Recommended for most users   |
| **Beta**   | [v1.8.7.3-beta](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.8.7.3-beta)                                            | Includes new features        |

<!-- versions:end -->

---

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/M4M71JOT9R)

---

## 🎥 New to Jellyfin 2 Samsung?

Not sure where to start? No worries — we've made it easy.

Before diving in, check out this clear, step-by-step video guide that walks you through the entire installation process:

👉 **Watch the YouTube tutorial:** 
https://www.youtube.com/watch?v=_8mSV5pW-ic

It’s the quickest way to get set up with confidence.

---

> 📺 **Older Samsung TVs**  
> Samsung TVs running **Orsay OS (the predecessor of Tizen)** are not supported by this installer.  
> For those devices, use the **Jellyfin Orsay Installer** instead:  
> 👉 https://github.com/PatrickSt1991/Jellyfin-Orsay-Installer

---

## 📦 Community Packages (New!)

Looking for older builds, custom `.wgt` files, or versions shared by other users?  
Check out the new **Tizen Community Packages** repository — a curated collection of community-submitted packages.

👉 **Browse community packages:**  
https://github.com/PatrickSt1991/tizen-community-packages


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
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/235ea81e-2567-402b-8631-9b65ee27ef62" />


The tool automatically scans your local network for compatible Samsung Smart TVs.

---

### 2. Select a Release  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/b91d0537-dbcf-489f-83ae-f2ff2c6b4b11" />

Choose the desired Jellyfin release.

---

### 3. Pick a Version  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/c6c74098-c0e9-4ccb-ad23-3b9ca1836e6e" />

Select the specific Jellyfin version you’d like to install.

---

### 4. Select Your TV  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/b028b8ef-e309-4967-bce2-8557cb5533f0" />

The tool lists all detected Samsung TVs. You can also manually enter an IP if your TV isn’t found.

<img width="422" height="252" alt="image" src="https://github.com/user-attachments/assets/88a975b9-e590-4c46-93ac-838f728bdb72" />

---

### 5. Sit Back and Watch the Magic ✨  
<img width="582" height="442" alt="image" src="https://github.com/user-attachments/assets/65443580-7ec1-481e-bc3f-63218f97d30d" />

Once started, the installer takes care of everything else automatically.

---

### ⚠️ Certificate Required

<img src="https://github.com/user-attachments/assets/b32a5873-a9d5-4f1e-9266-69f33961917f" alt="Tizen Email" width="400">
<img src="https://github.com/user-attachments/assets/9ad45a0a-f091-4eb6-94e8-eb0f381816d2" alt="Tizen Password" width="400">

A **Samsung account is required only when a new Samsung security certificate must be created or regenerated**. This usually occurs if the app package is modified, such as enabling plugins, changing permissions, or adjusting install requirements.

If your **Jellyfin app—or any other app—requires a new certificate**, you must **log in with a Samsung account** to generate the required security certificates before the app can be installed or updated.

---

## ⚙️ Settings

### Language
<img width="612" height="782" alt="image" src="https://github.com/user-attachments/assets/0e3da411-cb2e-4da3-9c0d-46c51b715973" />

Select your preferred language.

### Certificates
<img width="612" height="782" alt="image" src="https://github.com/user-attachments/assets/1215fdef-8a1f-4dfa-92e4-9074c5aebbac" />


Choose an existing certificate or let the tool generate a new one automatically.

### Advanced Options
<img width="612" height="782" alt="image" src="https://github.com/user-attachments/assets/89decada-4f7e-4b82-a5d8-09e32e239f90" />   

- **Custom WGT:** Upload your own `.wgt` file(s). The package name is randomized to allow side-by-side installs.
- **Local IP:** Your device’s local network IP address, entered as the **Host IP** in Samsung Developer Mode.
- **Overwrite Existing Version:** Attempts to overwrite an existing Jellyfin installation.
- **Open After Installation:** Automatically launches Jellyfin after installation.
- **Remember IPs:** Saves a manually entered IP address when the device is not found via scanning (one IP at a time).
- **Remove Old Jellyfin:** Attempts to uninstall previous Jellyfin versions before installing (not supported on all TVs).
- **Force Samsung Login:** Forces a Samsung account login to generate a new security certificate.
- **Right-to-Left Reading:** For right-to-left languages, the IP address must be inverted for Tizen Studio to work (e.g. `192.168.1.2` → `2.1.168.192`).
- **Jellyfin Config:** Preconfigure the Jellyfin app so no setup is required on the TV after installation.
 

## 📝 Jellyfin Config
<img width="690" height="932" alt="image" src="https://github.com/user-attachments/assets/c3561f78-c6d3-4027-b130-f56457b70941" />


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

| Setting | Description |
|--------|-------------|
| **Server IP** | Pre-fills the Jellyfin server address in the app configuration, so you don’t need to enter it on the TV. |
| **API Key** | Required for Jellyfin-specific actions, such as loading and applying user settings. |
| **Jellyfin User(s)** | Select a specific user or all users to apply browser or user-level settings. |
| **Enable TV Debug** | Establishes a WebSocket connection between the Jellyfin app and the device running this tool to assist with debugging when the app fails to start or behaves unexpectedly. |
| **Jellyfin Plugins** | Retrieves installed plugins from the Jellyfin server and modifies them for compatibility with Samsung TVs. |

### Browser Settings

| Setting | Description |
|--------|-------------|
| **Enable Backdrops** | Displays background images behind library and item views. |
| **Enable Theme Songs** | Plays theme music when browsing libraries or items. |
| **Enable Theme Videos** | Plays theme videos in supported views. |
| **Backdrop Screensaver** | Uses backdrops as a screensaver when the app is idle. |
| **Details Banner** | Shows a banner image on item detail pages. |
| **Cinema Mode** | Automatically plays trailers before starting media playback. |
| **Next Up Enabled** | Displays the *Next Up* section for continued watching. |
| **Enable External Video Players** | Allows playback using external video players when supported. |
| **Skip Intros** | Enables automatic skipping of episode intros when available. |
| **Audio Language Preference** | Sets the preferred default audio language for playback. |
| **Subtitle Language Preference** | Sets the preferred default subtitle language. |
| **Theme** | Selects the app’s visual theme (e.g. Light or Dark). |
| **Subtitle Mode** | Controls how subtitles are displayed by default. |

### User Settings

| Setting | Description |
|--------|-------------|
| **Auto Play Next Episode** | Automatically plays the next episode in a series when the current one ends. |
| **Remember Audio Selections** | Remembers the selected audio track for future playback. |
| **Remember Subtitle Selections** | Remembers the selected subtitle track for future playback. |
| **Play Default Audio Track** | Always plays the default audio track when starting playback. |
| **Enable Auto Login** | Automatically signs in to the selected user when the app starts. |

---

## ✅ Requirements

Before getting started, make sure you have:

- An active **internet connection** (required for generating or exchanging security certificates)
- A **Samsung Tizen Smart TV** with **Developer Mode enabled**
- A **default web browser** installed on your device
- A valid **[Samsung account](https://account.samsung.com/iam/signup)** (required only when a new Samsung certificate must be created)

---

## 🛠️ Support, Feedback & Wiki

Need help or want to report a bug?  
👉 [Open an issue](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  
📖 [Check the wiki](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/wiki)  

Got an idea for improvement?  
💡 Share it on the [Discussions board](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/discussions)  
or [submit a feature request](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues)  

We welcome all contributions and feedback to improve the experience for everyone!

