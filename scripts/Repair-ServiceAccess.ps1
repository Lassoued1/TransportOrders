#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$DatabasePath)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path (Join-Path $PSScriptRoot '..\artifacts\service-access-repair.log') -Append
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FolderAclWriter {
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern bool SetFileSecurity(string path, uint information, byte[] descriptor);
}
'@
$databaseFile = [IO.Path]::GetFullPath($DatabasePath)
$dataRoot = Split-Path -Parent $databaseFile
# Traverse ancestors without granting directory listing or access to other user files.
$ancestor = Split-Path -Parent $dataRoot
while ($ancestor -and $ancestor -ne [IO.Path]::GetPathRoot($ancestor)) {
    $acl = Get-Acl -LiteralPath $ancestor
    $sid = New-Object Security.Principal.SecurityIdentifier('S-1-5-19')
    $rights = [Security.AccessControl.FileSystemRights]::Traverse
    if ($ancestor -eq (Split-Path -Parent $dataRoot) -or $ancestor -eq (Split-Path -Parent (Split-Path -Parent $dataRoot))) {
        $rights = $rights -bor [Security.AccessControl.FileSystemRights]::ReadAttributes
    }
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, $rights, 'None', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    if (-not [FolderAclWriter]::SetFileSecurity($ancestor, 4, $acl.GetSecurityDescriptorBinaryForm())) {
        throw "Cannot grant traversal: $ancestor; Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"
    }
    $ancestor = Split-Path -Parent $ancestor
}
Start-Service TransportOrders.Runtime
(Get-Service TransportOrders.Runtime).WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Invoke-RestMethod 'http://localhost:5080/api/runtime' -UseDefaultCredentials -TimeoutSec 15 | ConvertTo-Json
Stop-Transcript
