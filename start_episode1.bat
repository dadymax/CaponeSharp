@echo off
rem Starts Capone (C# port) straight into episode 1, without the main menu and the briefing.
rem Remove --skip-briefing to see the episode briefing first.
cd /d "%~dp0"
"..\godottry\godot\Godot_v4.7.2-stable_mono_win64_console.exe" --path . -- --episode=1 --skip-briefing
