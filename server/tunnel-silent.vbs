Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
cfg = sh.ExpandEnvironmentStrings("%USERPROFILE%") & "\.cloudflared\board.yml"
exe = "cloudflared"
local = sh.ExpandEnvironmentStrings("%ProgramFiles(x86)%") & "\cloudflared\cloudflared.exe"
If fso.FileExists(local) Then exe = """" & local & """"
sh.Run exe & " --config """ & cfg & """ --no-autoupdate tunnel run nexus-board", 0, False
