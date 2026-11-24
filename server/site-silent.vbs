Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "E:\Nexus-Board"
sh.Run "node ""E:\Nexus-Board\server\server.js""", 0, False
