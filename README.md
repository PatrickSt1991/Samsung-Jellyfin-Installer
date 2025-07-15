# Jellyfin 2 Samsung (Samsung-Jellyfin-Installer)

A simple tool for installing **Jellyfin** on your **Samsung Smart TV**—quickly and effortlessly.  
✅ **Supports all Tizen versions**

> Huge thanks to [jeppevinkel](https://github.com/jeppevinkel/jellyfin-tizen-builds) for providing the Jellyfin Tizen `.wgt` builds—super helpful and much appreciated!

---
## 🔥[Check out the official page for Jellyfin 2 Samsung](https://patrickst1991.github.io/Samsung-Jellyfin-Installer/)
## 📦 Current Versions

<!-- versions:start -->

| Channel    | Version                                | Notes                                      |
|------------|----------------------------------------|--------------------------------------------|
| **Stable** | [`v1.6.2`](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.6.2)         | Recommended for most users                 |
| **Beta**   | [`v1.6.4-beta`](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/releases/tag/v1.6.4-beta)             | Includes new features, may be less stable  |

<!-- versions:end -->

---

## 🚀 How It Works

### 1. Launch the Tool  
![Start screen](https://github.com/user-attachments/assets/4b0b0ba6-1165-4ce9-ac62-255baed6b21b)  
The tool automatically scans your local network for compatible Samsung Smart TVs.

---

### 2. Select a Release  
![Choose the release](https://github.com/user-attachments/assets/34b0518e-11a6-49f7-8bcc-055267fa6a3d)  
Choose the desired Jellyfin release.

---

### 3. Pick a Version  
![Choose the version](https://github.com/user-attachments/assets/935313d4-3db4-4e02-beeb-a4a1ceae2739)  
Select the specific Jellyfin version you’d like to install.

---

### 4. Select Your TV  
![Devices_found](https://github.com/user-attachments/assets/d9aba234-c73a-480e-842d-2a7998c3ce6c)  
The tool lists all detected Samsung TVs. You can also manually enter an IP if your TV isn’t found.

![Device_not_listed](https://github.com/user-attachments/assets/d9272aad-562a-4485-b52f-885652cd720b)  

---

### 5. Sit Back and Watch the Magic ✨  
![See the magic happen](https://github.com/user-attachments/assets/3826c8ec-51d1-4f08-8a3c-cf5cdd6bbe36)  
Once started, the installer takes care of everything else automatically.

---

### ⚠️ Special Notes for Tizen 7+

<img src="https://github.com/user-attachments/assets/b32a5873-a9d5-4f1e-9266-69f33961917f" alt="Tizen Email" width="400">
<img src="https://github.com/user-attachments/assets/9ad45a0a-f091-4eb6-94e8-eb0f381816d2" alt="Tizen Password" width="400">

Tizen 7+ requires a **Samsung account login** during the install. This step is necessary for generating and exchanging the security certificates used for app installation.

---

## ⚙️ Settings

### Language
![SetLang](https://github.com/user-attachments/assets/a1e672e0-dfed-4a47-a055-655d09601a2f)  
Select your preferred language.

### Certificate
![SetCert](https://github.com/user-attachments/assets/e3ede4b0-40b4-4a8c-966d-74643e1ea0f4)  
Choose an existing certificate or let the tool generate a new one automatically.

### Advanced Options
![SetGen](https://github.com/user-attachments/assets/1d6c7659-44fa-40e0-a5f0-d7b60f8e7e76)  
- **Custom WGT:** Upload your own `.wgt` file (randomizes the package name to allow side-by-side installs).  
- **Remember IPs:** Save a manually entered IP when your device isn’t found via scan (one IP at a time).  
- **Remove Old Jellyfin:** Attempts to uninstall previous versions before installation (not supported on all TVs).
- **Force Samsung Login:** Force the tool to login in order to forcefully create a new certificate.
- **Right-to-left Reading:** Languages the are right-to-left need to have the IP inverted in order for Tizen Studio to work (192.168.1.2 -> 2.1.168.192)

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
