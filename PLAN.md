# World actions (feat/world-actions)

Design rule: risky actions and gambles happen in the world. Dialogs only offer clear deals.

## Decisions
- Item actions live in the item's resource rules (`HotCargoActions`). A postfix on the `ResourceRules` constructor reads them from the same rules node.
- Buttons: a postfix on the vanilla `PartsCard` constructor adds one button per action, in the same commands box as the trigger and toggle buttons. A button shows while a selected storage part holds a unit of its item.
- A press sends `HotCargoItemAction` (MP input, part reference like vanilla `MPCancelRepairInput`). It acts on one unit in that part.
- An action replaces the unit with `Becomes` (same tile) and rolls one of its weighted `Contents` into that storage, or loose when it is full.
- Sealed Case "Open the case" -> Opened Case + Corporate Access Codes (usual) or Tracer Beacon (heat-weighted).
- Tracer Beacon: heat each minute while in a player ship. Its own action "Space it" destroys it.
- Codes sell at stations through a hail line (clear deal, like Off the Books).
- Hand-over accepts the item and its `Becomes` forms. Opened case outcomes: unnoticed / pays 20 % less / hostile / leaves (new outcome fields `HandOverPay`, `ContactTurns`, `Chat`).
- Accept button: re-evaluate the no-room state every frame while the offer is selected.

## Steps
1. Item action rules, button, MP input, test commands.
2. Sealed case, opened case, codes, tracer; courier page.
3. Hand-over reactions.
4. Accept button live update.
5. Prototype haggle, Extraction strip: proposals and conversions.
6. Spec 46 (1111-1130), design rule, docs.
7. Tests: single-player and co-op on the Mac.
