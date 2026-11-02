#!/usr/bin/env python3
# rapid-ocr-cli.py — 對一個目錄的 linecrops 做 RapidOCR 只識別，輸出 JSON。
# 用法：rapid-ocr.exe <cropDir> <outJson>
import sys, os, json, time
from rapidocr_onnxruntime import RapidOCR

def main():
    if len(sys.argv) < 3:
        print('usage: rapid-ocr.exe <cropDir> <outJson>', file=sys.stderr)
        return 2
    crop_dir, out_path = sys.argv[1], sys.argv[2]
    engine = RapidOCR()
    result = {}
    for name in sorted(os.listdir(crop_dir)):
        if not name.lower().endswith(('.png', '.jpg', '.jpeg')):
            continue
        p = os.path.join(crop_dir, name)
        t0 = time.time()
        try:
            res, _ = engine(p, use_det=False, use_cls=False, use_rec=True)
            if res:
                text, score = res[0][0], float(res[0][1])
                result[name] = {'text': text, 'score': round(score, 4), 'ms': int((time.time()-t0)*1000)}
            else:
                result[name] = {'text': '', 'score': 0, 'ms': int((time.time()-t0)*1000)}
        except Exception as e:
            result[name] = {'text': '', 'score': 0, 'error': str(e), 'ms': int((time.time()-t0)*1000)}
    with open(out_path, 'w', encoding='utf-8') as f:
        json.dump(result, f, ensure_ascii=False)
    print(json.dumps({'ok': True, 'count': len(result), 'out': out_path}, ensure_ascii=False))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
