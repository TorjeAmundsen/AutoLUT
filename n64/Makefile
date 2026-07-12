all: autolut-palette.z64
.PHONY: all

BUILD_DIR = build
include $(N64_INST)/include/n64.mk

OBJS = $(BUILD_DIR)/main.o

autolut-palette.z64: N64_ROM_TITLE = "AUTOLUT PALETTE"

$(BUILD_DIR)/autolut-palette.elf: $(OBJS)

clean:
	rm -rf $(BUILD_DIR) *.z64
.PHONY: clean

-include $(wildcard $(BUILD_DIR)/*.d)
