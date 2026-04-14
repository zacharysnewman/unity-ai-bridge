# JSON Scenes — Test Cases

## JSON → Scene

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| J1 | Create JSON file | Object spawned in scene | PASSED | |
| J2 | Edit transform | Object moves/rotates/scales | PASSED | |
| J3 | Edit name | Object renamed in hierarchy | PASSED | |
| J4 | Add customData entry | Component added to object | PASSED | |
| J5 | Edit customData field | Component field updated | PASSED | |
| J6 | Remove customData entry | Component removed from object | PASSED | |
| J7 | Edit parentUuid | Object reparented | KNOWN LIMITATION | Hierarchy tree only rebuilds when Unity has focus — Unity editor windowing constraint, not fixable via API |
| J8 | Delete JSON file | Object destroyed | PASSED | Includes child cleanup when parented |

## Editor → JSON

| # | Action | Expected | Status | Notes |
|---|---|---|---|---|
| E1 | Create object | New JSON file created | PASSED | |
| E2 | Duplicate object | New JSON file with new UUID | PASSED | |
| E3 | Move/rotate/scale object | JSON transform updated | PASSED | |
| E4 | Rename object | JSON name updated | PASSED | |
| E5 | Add component | JSON customData entry added | PASSED | |
| E6 | Modify component field | JSON customData field updated | PASSED | |
| E7 | Remove component | JSON customData entry removed | PASSED | |
| E8 | Reparent object | JSON parentUuid updated | PASSED | |
| E9 | Delete object | JSON file deleted | PASSED | |
