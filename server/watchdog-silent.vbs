Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "E:\Nexus-Board\server"
sh.Run "node ""E:\Nexus-Board\server\watchdog.js""", 0, False
