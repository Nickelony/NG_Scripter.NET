; === Exotic commands in nested include ===
; This tests that #INCLUDE works from within another include
; and that the > continuation works inside included files.

; === > continuation inside nested include ===
TriggerGroup= 27, $2000, 406, >
 16, 17, 18

; === Define in nested include (file-scoped to exotic_commands.inc) ===
#define NESTED_DEFINE 42

; === Command using the nested define ===
TriggerGroup= 28, NESTED_DEFINE, 406, 19, 20, 21

; === Tab-indented command in include ===
	Customize= 21,6,7,8,9

; === > continuation with mixed whitespace ===
Parameters= 29, $200, >
  	  6,  7,  8
