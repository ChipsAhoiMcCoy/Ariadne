# Player feedback regression checks

Build validation: `Tools/build.ps1` packages successfully with no compiler warnings or errors. `dotnet run --project TestBenches/GameplayLogicHarness/GameplayLogicHarness.csproj` passes, including repeated crafting's recipe latch and single activation for equipment.

The following are live gameplay checks to perform after reloading the mod. They have not been run by the coding agent.

1. Walk across track at body/foot height on supported ground, then beneath overhead track within four tiles. Expect the short, bright descending tick and a spoken track label at the start of a run. Check both directions and inverted gravity. Platforms and ropes should retain their cues.
2. Open Ariadne Accessibility Settings. Toggle **Passive radar finds dropped items**, save, reopen, and check persistence. With it off, apostrophe must still include dropped item stacks. Pick up coins between apostrophe presses less than four seconds apart: the next press must not read those coins. Also test a partially collected stack and another drop reusing its world item slot.
3. Hold I on metal bars, then release. Check the output and consumed ore. Exhaust the ore while holding I: the next recipe must not be crafted. Press I again on another recipe. Hold I on equipment: only one piece should be made per press.
4. Craft gold bars, silver bars, a gold pickaxe, and three different tungsten armor pieces without leaving crafting. Close inventory and check every output. Repeat with full inventory: any overflow stays held and further crafting stops without spending ingredients until the held item can be stored.
5. Navigate all 40 item slots and each action in a chest, Piggy Bank, Safe, Defender's Forge, and Void Vault. Item positions should be 1 of 40 through 40 of 40; actions should not claim a slot number. Repeat with multiple storage columns and check that all actions remain reachable.
6. Cursor across different banner styles, including styles in later sprite rows, and multiple statue types facing both directions. Check each tile of each object, and compare its spoken name with the inventory item after removal.
7. Fish with Sonar Potion active. Check ordinary fish, crates, enemy catches, repeated bites of the same type, and a modded custom sonar popup if available. Each new catch popup should speak once. Without Sonar Potion, catch names should remain silent.
