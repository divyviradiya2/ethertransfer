#!/bin/bash
echo "=========================================="
echo " Building EtherTransfer Linux Release"
echo "=========================================="

PUBLISH_DIR="publish/linux-framework-dependent"

# 1. Clean the old publish folder
if [ -d "$PUBLISH_DIR" ]; then
    rm -rf "$PUBLISH_DIR"/*
fi

# 2. Build the .NET App
echo -e "\n[1/1] Compiling C# Code (Release, linux-x64, Framework-Dependent)..."
dotnet publish "EtherTransfer.UI/EtherTransfer.UI.csproj" -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=None -o "$PUBLISH_DIR"

if [ $? -ne 0 ]; then
    echo -e "\nBuild failed! Aborting."
    exit 1
fi

echo -e "\n✅ SUCCESS! Your Linux binaries are ready at:"
echo "   $(pwd)/$PUBLISH_DIR"
echo -e "\n🎉 Build script finished!"
