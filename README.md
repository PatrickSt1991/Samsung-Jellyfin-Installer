# Jellyfin 2 Samsung  (Samsung-Jellyfin-Installer)

Tool for installing Jellyfin on your Samsung Smart TV

## Requirements
- Samsung Tizen TV (with developer mode enabled)  
- The IP address of the TV  
- Network connection between your computer and the TV

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
