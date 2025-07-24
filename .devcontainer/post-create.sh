#!/bin/bash

set -e

WORKSPACE_FOLDER=$(pwd)

echo "Installing functions-core-tools:"

tmp_folder=$(mktemp -d)
install_folder=/opt/microsoft/azure-functions-core-tools
cd $tmp_folder

wget -q "https://github.com/Azure/azure-functions-core-tools/releases/download/4.1.0/Azure.Functions.Cli.linux-x64.4.1.0.zip"

echo " - extracting files."
unzip -q Azure.Functions.Cli.linux-x64.4.1.0.zip
rm Azure.Functions.Cli.linux-x64.4.1.0.zip
chmod +x func
chmod +x gozip
sudo mkdir -p $install_folder
sudo rsync -av $tmp_folder/ $install_folder
rm -rf $tmp_folder

echo " - export func."
sudo ln -fs $install_folder/func /usr/local/bin/func

cd "$WORKSPACE_FOLDER"

# Ensure latest version of azd
curl -fsSL https://aka.ms/install-azd.sh | bash
