# Put this file anywhere (C:\Tools, etc.) and add its parent to Windows PATH
# Change the below if your username in WSL is not the same as on Windows
$wslUser = $env:USERNAME
$winPath = $PWD.Path
$drive = $winPath.Substring(0,1).ToLower()
$tail = $winPath.Substring(2).Replace('\','/')
$wslPath = "/mnt/$drive$tail"
# Escape single quotes for bash: ' -> '\''
$safePath = $wslPath -replace "'", "'\''"
# Build the command string with the absolute path to opencode
# .bashrc (which adds "opencode" as something runnable) is not active in non-interactive mode
# wsl bash -c is non-interactive and can't find "opencode"
$bashCmd = "cd '$safePath' && /home/$wslUser/.opencode/bin/opencode"
foreach ($arg in $args) {
    $safe = $arg -replace "'", "'\''"
    $bashCmd += " '$safe'"
}
# PowerShell passes the entire $bashCmd variable as a SINGLE argument to bash -c
wsl bash -c $bashCmd
