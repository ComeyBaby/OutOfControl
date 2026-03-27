extends Control

@onready var host_checkbox = $HostingSection/HostServer
@onready var join_checkbox = $HostingSection/JoinServer

@onready var win_condition = $WinConditionSection/WinCondition
@onready var points_spinbox = $WinConditionSection/PointsSpinBox

@onready var game_mode = $GameModeSection/GameMode

@onready var ip_input = $JoinSection/IP
@onready var room_code_input = $JoinSection/RoomCode

func _ready():
	host_checkbox.toggled.connect(_on_host_toggled)
	join_checkbox.toggled.connect(_on_join_toggled)
	game_mode.item_selected.connect(_on_game_mode_selected)

func _on_host_toggled(pressed):
	if pressed:
		join_checkbox.button_pressed = false

func _on_join_toggled(pressed):
	if pressed:
		host_checkbox.button_pressed = false

func _on_game_mode_selected(index):
	var mode = game_mode.get_item_text(index)
	print("Selected mode:", mode)

func get_game_settings():
	return {
		"hosting": host_checkbox.button_pressed,
		"joining": join_checkbox.button_pressed,
		"win_condition": win_condition.get_selected_id(),
		"points": points_spinbox.value,
		"game_mode": game_mode.get_item_text(game_mode.selected),
		"ip": ip_input.text,
		"room_code": room_code_input.text
	}
