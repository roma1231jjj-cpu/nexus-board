Set sh = CreateObject("WScript.Shell")
sh.Run """C:\Program Files (x86)\cloudflared\cloudflared.exe"" --config ""C:\Users\roma1\.cloudflared\board.yml"" --no-autoupdate tunnel run nexus-board", 0, False
