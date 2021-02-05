
# Extra pre-build step to install a newer .NET SDK than what CircleCI has,
# because we need to be able to build for .NET 5.

# See: https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script

& "./.ldrelease/dotnet-install.ps1" -Channel 5.0
[Environment]::SetEnvironmentVariable("Path", "C:\Users\circleci\AppData\Local\Microsoft\dotnet\;$env:Path", [System.EnvironmentVariableTarget]::Machine)
