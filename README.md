# NXInput (formerly SwitchXBOXController)
Turn your Nintendo Switch into an Xinput wireless controller for your Windows computer!

Based on [WerWolv](https://github.com/purnasth)'s great work, now working to the latest Switch's FW (ATTOW 21.1.0) by upgrading to libnx 4.10.
Why this name change? Well, I didn't like the old name, and I think that NXInput makes a good pun (NX - XInput).
As ScpDriverInterface is deprecated and can cause compatibility issues with Windows 11, this was migrated to yet another deprecated but much more recent driver ([ViGEmBus](https://github.com/nefarius/ViGEmBus)), as we currently don't have any better alternative, and many of us likely already have this installed for some other controller related software.
The client and the server will now be distributed as separate zip files, so keep sure to download both of them.

![Server](https://puu.sh/BVASI/d8c6c00ecc.png)

## How to use
- Download the latest [ViGEmBus](https://github.com/nefarius/ViGEmBus) release and install it if you don't already have it.
- Download the latest NXInput release and execute `NXInput_Server.exe`. You might have to allow network access in the firewall settings.
- Put the SwitchXBOXController_Client.nro in your `/switch` folder on the SD Card of your Nintendo Switch.
- Start the homebrew application using the hbmenu

## Roadmap
 1. ~~Upgrade client to libnx 4.10~~ ✅
 2. (soon) Modify server to use ViGEmBus
 3. (later) Add USB connection method (still using the Server)
 4. TBD - Switch-side XInput USB emulation (no Server or drivers needed in this case, and would work with any XInput compatible device, even with an XBOX and Android Phones)

## Technical notes & Troubleshooting
Before saying: 
> This doesn't work!

*This works. It just doesn't work the way you think it would (not yet). It's required to keep your Switch connected to the same network of your PC, as it will send controller data through UDP packets!*

This application works by using a UDP socket between your Switch and your PC, transmitting your controller's data (defaults to the handheld/1st player's controller), so you could technically bridge a Switch Pro Controller connected to your Switch to your PC.

That could make absolutely no sense, but imagine not having Bluetooth in 2025! This would solve your issues by adding up some cool additional latency! 😂

Jokes apart, it could be really handy having an ergonomic Switch Lite and using it as a controller, in case you don't have one. Sure, Hekate now includes a USB Gamepad function, but that's limited to D-Input and lazy people like me don't want either to use a XInput wrapper and to reboot to Hekate every time.

*So what does lazy people like to do?* Spending a day rewriting an old piece of software, just to use it inside HOS *without* rebooting 🔥!

Originally, this application was designed to use a UDP broadcast packet, but modern routers tend to keep the broadcast address exclusive to things like DHCP, ARP, NDP and WoL, automatically dropping "custom" packets. This was the case for me on both an OpenWRT router and an ISP-branded one.

While the UDP logic hasn't changed (only subtle optimizations), it is advisable to use your PC's local IP address instead of the broadcast address of your network (usually the latest IP address, XXX.XXX.XXX.255, assuming you have a 24-bit subnet mask), as that's guaranteed to work properly. If you want to experiment, you could try to use the broadcast address of your network, in theory on some "older" (802.11ac or earlier, I guess) routers it should work just fine.
As some routers don't even default to "the 255" address, I decided to ditch the advice from the Switch's Client, suggesting instead to use the IP address of the PC running the server.
