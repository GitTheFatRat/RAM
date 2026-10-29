Context: I am developing a C# WPF application ("RbxManager") that automates multiple Roblox instances on a Dual Xeon workstation. I am facing a critical ban-wave issue:
- Account 1 got flagged by Hyperion/Byfron with a warning: "We noticed a modified version of Roblox was used on a device you recently played on."
- Immediately after, when trying to launch Accounts 2, 3, and 4 from the same machine, they didn't launch into the game but got stuck in a CAPTCHA verification loop at login.

Task: You are the lead system architect and reverse-engineer. I want you to figure out the root cause and fix it autonomously.
1. Scan the codebase (focusing on Launcher, HttpClient, and Session/Process management) using your tools.
2. Analyze exactly how Roblox's anti-bot system is fingerprinting our network/hardware to link these accounts together and detect the automation.
3. Formulate a complete strategy to bypass this fingerprinting and prevent the CAPTCHA loop.
4. Execute the code changes directly to implement your own solutions.

Do not ask me for ideas or solutions. Analyze the symptoms and implement the fix.