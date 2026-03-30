extends Control

@onready var options_button = VBoxContainer/OptionsButton

func _ready():
	options_button.pressed.connect(_on_options_pressed)

func _on_options_pressed():
	get_tree().change_scene_to_file("res://ui/OptionsMenu.tscn")
