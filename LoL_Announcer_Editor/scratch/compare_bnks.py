import json

def analyze_bnk(path):
    with open(path, 'r') as f:
        data = json.load(f)
    
    counts = {}
    for obj in data['sections']:
        obj_type = obj.get('type', 'Unknown')
        counts[obj_type] = counts.get(obj_type, 0) + 1
    
    return counts

before = analyze_bnk(r'C:\Users\walid\Desktop\bnk_dump_before.json')
after = analyze_bnk(r'C:\Users\walid\Desktop\bnk_dump_after.json')

print("Type\tBefore\tAfter")
all_types = set(before.keys()) | set(after.keys())
for t in sorted(all_types):
    print(f"{t}\t{before.get(t, 0)}\t{after.get(t, 0)}")
