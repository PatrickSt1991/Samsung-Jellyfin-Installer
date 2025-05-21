# Jellyfin 2 Samsung  (Samsung-Jellyfin-Installer)

A simple tool to install Jellyfin on your Samsung Smart TV with ease.
 - The tool can be used for Tizen 7 and up but it's in beta, please report any errors found.

Big shoutout to [jeppevinkel](https://github.com/jeppevinkel/jellyfin-tizen-builds) for sharing the Jellyfin Tizen .wgt files—super helpful and much appreciated!

## How It Works

Follow these steps to get Jellyfin up and running on your TV:

### 1. Launch the Tool

![Start screen](https://github.com/user-attachments/assets/3a846291-068b-4c32-9453-27704974ef32)
_The start screen of the installer._

---

### 2. Select a Release

![Choose the release](https://github.com/user-attachments/assets/8e85b079-fdc0-4fba-bd4d-94ba9f00d9da)  
_Pick the desired Jellyfin release._

---

### 3. Pick a Version

![Choose the version](https://github.com/user-attachments/assets/10849d7b-313a-454b-addf-f0e6e348f63f)  
_Select the version you want to install._

---

### 4. Enter Your TV’s IP Address

![Fill in the IP address](https://github.com/user-attachments/assets/740cb166-b77d-4991-90a7-f6fcd09cc840)  
_Provide the IP address of your Samsung Smart TV._

---

### 5. Sit Back and Watch the Magic Happen ✨

![See the magic happen](https://github.com/user-attachments/assets/40e28dca-f741-4df1-904b-e2db975f68d6)  
_Installation begins. Your TV will handle the rest!_

---

## Requirements
To use this tool, make sure you have the following:

- A **Samsung Tizen TV** with **Developer Mode enabled**  
- The **IP address** of your TV  
- A **network connection** between your computer and the TV (both must be on the same network)  
- **Tizen Web CLI 5.5** — if not found in the usual locations, the tool will automatically download and install it for you

## Steps for Use

### 1. Enable Developer Mode on Samsung TV
Follow these steps to enable developer mode:

1. Press the **Home**/**Smart Hub** button on your remote control  
2. Go to **Apps**  
3. In the Apps menu, enter the number **12345** using the remote control or the on-screen keyboard  
   - This will open a new menu  
4. Turn **Developer Mode** on  
5. Under the "IP address" option, enter the IP address of your computer (the one running Jellyfin 2 Samsung)  
   - If you don’t know it, open Command Prompt and run `ipconfig` (on Windows) or open Terminal and run `ifconfig` (on Mac/Linux) to find your local IP address  

### 2. Find the TV’s IP Address

1. Go to **Settings** > **General** > **Network** > **Network Status**  
2. The IP address will be shown under "IP Address" or "Network Information"  
   - For example: `192.168.1.100`

### 3. Using Jellyfin 2 Samsung

1. Make sure your computer is connected to the same network as the TV  
2. Enter the TV’s IP address into the tool  
3. If the app is installed, it will automatically launch on the TV  

## Support

Having issues? Open an issue on our [GitHub repository](https://github.com/PatrickSt1991/Samsung-Jellyfin-Installer).
