# Jellyfin 2 Samsung (Samsung-Jellyfin-Installer)

A simple tool to install Jellyfin on your Samsung Smart TV with ease.  
✅ Supports **Tizen 7 and up**

Huge thanks to [jeppevinkel](https://github.com/jeppevinkel/jellyfin-tizen-builds) for providing the Jellyfin Tizen `.wgt` builds—super helpful and much appreciated!
---

## 📦 Current Versions

<!-- versions:start -->
_TODO: Version table will be auto-generated here_
<!-- versions:end -->

---

## 🚀 How It Works

Follow these steps to get Jellyfin running on your Samsung TV:

### 1. Launch the Tool

![Start screen](https://github.com/user-attachments/assets/2970399f-f2f6-45d5-9901-c400b7b75e19)  
_When launched, the tool will automatically scan your network for Samsung Smart TVs._

---

### 2. Select a Release

![Choose the release](https://github.com/user-attachments/assets/4b080475-bccf-4ae9-9090-c78e0aeefd7b)  
_Choose the Jellyfin release you want to install._

---

### 3. Pick a Version

![Choose the version](https://github.com/user-attachments/assets/a3f64737-4d7d-4759-8a8a-9cc5023c4934)  
_Select the specific version you prefer._

---

### 4. Select Your TV

![Devices_found](https://github.com/user-attachments/assets/d9aba234-c73a-480e-842d-2a7998c3ce6c)  
_All discovered devices will be listed, including an option to manually enter an IP._

If your device isn’t listed (e.g., on a different VLAN), you can manually enter the IP address:

![Device_not_listed](https://github.com/user-attachments/assets/d9272aad-562a-4485-b52f-885652cd720b)  

---

### 5. Sit Back and Watch the Magic Happen ✨

![See the magic happen](https://github.com/user-attachments/assets/351f59f2-34ec-4974-a87c-ab11c9f9a902)  
_The tool handles the rest—just relax and let the installation finish._

---

### 5.1 Special Notes for Tizen 7+

<img src="https://github.com/user-attachments/assets/b32a5873-a9d5-4f1e-9266-69f33961917f" alt="Tizen Email" style="width:400px; max-height:240px;">
<img src="https://github.com/user-attachments/assets/9ad45a0a-f091-4eb6-94e8-eb0f381816d2" alt="Tizen Password" style="width:400px; max-height:240px;">

Devices running Tizen 7+ will prompt for Samsung account login. This is required to generate and exchange the necessary certificates during installation.

---

## ✅ Requirements

Before you begin, make sure you have the following:

- A **Samsung Tizen Smart TV** with **Developer Mode enabled**
- **Tizen Web CLI**
- **Certificate manager**
- **Microsoft Edge WebView2 Runtime**
- A registered **[Samsung Account](https://account.samsung.com/iam/signup)** (required for Tizen 7+ installations)

> ℹ️ The installer will automatically check for missing dependencies and guide you through installing them if needed.

---

## 🧭 How to Enable Developer Mode on Your Samsung TV

1. Press the **Home** / **Smart Hub** button on your remote  
2. Navigate to **Apps**  
3. Enter **12345** using the remote or on-screen keyboard  
   - This opens the hidden Developer Mode menu  
4. Turn **Developer Mode** ON  
5. Under “IP address,” enter your PC’s IP address (the one running the installer)  
   - On Windows: `ipconfig` in Command Prompt  
   - On macOS/Linux: `ifconfig` in Terminal

---

## 🛠️ Support

Need help? Found a bug?  
👉 [Open an issue](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues) on the GitHub repository.

Have a feature request or an idea to improve the tool?  
💡 Start a new thread on the [Discussions board](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/discussions)  
or [open a feature request](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer/issues).

We welcome feedback and contributions to help improve the experience for everyone!
