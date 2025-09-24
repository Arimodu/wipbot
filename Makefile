include .env

clean:
	rm -rf ./wipbot/bin
	rm -rf ./wipbot/obj
	dotnet clean ./wipbot

build:
	dotnet build ./wipbot/wipbot.csproj -property:Configuration=Release
	cp ./wipbot/obj/Release/net48/wipbot.dll $(BS_DIR)/Plugins/