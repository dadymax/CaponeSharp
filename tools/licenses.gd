extends SceneTree

# Writes the Godot engine's licence notices (its own MIT licence and the third-party components built into it) to
# the file given after "--". Used by build-release.ps1:
#   godot --headless --path . --script res://tools/licenses.gd -- <output file>

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.is_empty():
		printerr("licenses.gd: give the output file after --")
		quit(1)
		return
	var out := FileAccess.open(args[0], FileAccess.WRITE)
	if out == null:
		printerr("licenses.gd: can't write ", args[0])
		quit(1)
		return
	out.store_string("Capone Reborn is built with the Godot Engine (https://godotengine.org) and .NET.\n\n")
	out.store_string("=== Godot Engine ===\n\n" + Engine.get_license_text() + "\n\n")
	out.store_string("=== Third-party components of the Godot Engine ===\n\n")
	for component in Engine.get_copyright_info():
		out.store_string("- " + str(component["name"]) + "\n")
		for part in component["parts"]:
			for line in part["copyright"]:
				out.store_string("  Copyright " + str(line) + "\n")
			out.store_string("  License: " + str(part["license"]) + "\n")
		out.store_string("\n")
	out.store_string("=== Licence texts ===\n\n")
	var licenses := Engine.get_license_info()
	for name in licenses:
		out.store_string("--- " + str(name) + " ---\n\n" + str(licenses[name]) + "\n\n")
	out.store_string("=== .NET runtime ===\n\n"
		+ "The .NET runtime in the data_CaponeSharp_windows_x86_64 folder is MIT-licensed,\n"
		+ "Copyright (c) .NET Foundation and Contributors: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT\n")
	out.close()
	quit(0)
