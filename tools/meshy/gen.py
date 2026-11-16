#!/usr/bin/env python3
"""Meshy text-to-3D: для каждого задания — preview (сетка) → refine (текстура) → скачать GLB и превью.
Все задания идут параллельно. Ключ — переменная MESHY_API_KEY (секрет Actions), в файлы не пишется.
Задание: {"id": "brig", "prompt": "...", "texture_prompt": "...", "polycount": 40000, "skip": false}
("skip": true — пропустить, например уже удачные при перегенерации остальных)."""
import json, os, sys, time, urllib.request, urllib.error

API = "https://api.meshy.ai/openapi/v2/text-to-3d"
I2I = "https://api.meshy.ai/openapi/v1/image-to-image"
I23 = "https://api.meshy.ai/openapi/v1/image-to-3d"
KEYS = {1: os.environ.get("MESHY_API_KEY", ""), 2: os.environ.get("MESHY_KEY2", "")}
KEY = KEYS[1]
import base64

def data_uri(path):
    return "data:image/png;base64," + base64.b64encode(open(path, "rb").read()).decode()

def call(method, url, body=None, key=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": "Bearer " + (key or KEY), "Content-Type": "application/json"})
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode() or "{}")
        except urllib.error.HTTPError as e:
            msg = e.read().decode(errors="replace")[:400]
            if e.code == 429 or e.code >= 500:
                time.sleep(10 * (attempt + 1)); continue
            raise RuntimeError(f"HTTP {e.code} {url}: {msg}")
        except Exception as e:
            time.sleep(5 * (attempt + 1)); last = e
    raise RuntimeError(f"нет ответа {url}")

def fetch(url, path):
    for attempt in range(5):
        try:
            with urllib.request.urlopen(url, timeout=300) as r, open(path, "wb") as f:
                f.write(r.read()); return os.path.getsize(path)
        except Exception:
            time.sleep(5 * (attempt + 1))
    raise RuntimeError("не скачать " + url)

def main():
    jobs_path, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    for n, k in KEYS.items():
        if not k: print("аккаунт", n, ": ключа нет"); continue
        try: print("аккаунт", n, "баланс:", call("GET", "https://api.meshy.ai/openapi/v1/balance", key=k))
        except Exception as e: print("аккаунт", n, "баланс не получен:", e)
    jobs = [j for j in json.load(open(jobs_path, encoding="utf-8"))["jobs"] if not j.get("skip")]
    concept = [j for j in jobs if j.get("concept")]
    jobs = [j for j in jobs if not j.get("concept")]
    if concept: run_concept(concept, out, os.path.dirname(jobs_path))
    if not jobs: return
    state = {}
    for j in jobs:
        if j.get("refine_of"):
            state[j["id"]] = {"job": j, "stage": "preview", "task": j["refine_of"]}; print(j["id"], "текстура к", j["refine_of"]); continue
        body = {"mode": "preview", "prompt": j["prompt"][:800], "ai_model": j.get("ai_model", "latest"),
                "should_remesh": True, "topology": "triangle", "target_polycount": int(j.get("polycount", 40000)),
                "target_formats": ["glb"], "origin_at": "bottom"}
        try:
            tid = call("POST", API, body)["result"]
            state[j["id"]] = {"job": j, "stage": "preview", "task": tid}
            print(j["id"], "preview", tid)
        except Exception as e:
            state[j["id"]] = {"job": j, "stage": "error", "error": str(e)}; print(j["id"], "ОШИБКА", e)
    deadline = time.time() + 80 * 60
    while time.time() < deadline and any(s["stage"] in ("preview", "refine") for s in state.values()):
        time.sleep(15)
        for jid, s in state.items():
            if s["stage"] not in ("preview", "refine"): continue
            try: t = call("GET", API + "/" + s["task"])
            except Exception as e: print(jid, "опрос:", e); continue
            st = t.get("status")
            if st in ("FAILED", "CANCELED"):
                s["stage"] = "error"; s["error"] = json.dumps(t.get("task_error")); print(jid, s["stage"], st, s["error"]); continue
            if st != "SUCCEEDED": continue
            if s["stage"] == "preview" and s["job"].get("only_preview"):
                d = os.path.join(out, jid); os.makedirs(d, exist_ok=True)
                try:
                    if t.get("thumbnail_url"): fetch(t["thumbnail_url"], os.path.join(d, "thumb.png"))
                    size = fetch(t["model_urls"]["glb"], os.path.join(d, "shape.glb")) if t.get("model_urls", {}).get("glb") else 0
                    json.dump({"id": jid, "preview": t["id"], "prompt": s["job"]["prompt"], "credits": t.get("consumed_credits"),
                               "shape_bytes": size}, open(os.path.join(d, "task.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
                    s["stage"] = "done"; s["preview"] = t["id"]; print(jid, "ФОРМА ГОТОВА", t["id"])
                except Exception as e:
                    s["stage"] = "error"; s["error"] = str(e); print(jid, "ОШИБКА скачивания", e)
                continue
            if s["stage"] == "preview":
                s["preview"] = t["id"]; s["preview_credits"] = t.get("consumed_credits")
                body = {"mode": "refine", "preview_task_id": t["id"], "enable_pbr": True, "texture_resolution": "2k",
                        "target_formats": ["glb"], "origin_at": "bottom"}
                if s["job"].get("texture_prompt"): body["texture_prompt"] = s["job"]["texture_prompt"][:800]
                try:
                    s["task"] = call("POST", API, body)["result"]; s["stage"] = "refine"; print(jid, "refine", s["task"])
                except Exception as e:
                    s["stage"] = "error"; s["error"] = str(e); print(jid, "ОШИБКА refine", e)
            else:
                d = os.path.join(out, jid); os.makedirs(d, exist_ok=True)
                try:
                    size = fetch(t["model_urls"]["glb"], os.path.join(d, "model.glb"))
                    if t.get("thumbnail_url"): fetch(t["thumbnail_url"], os.path.join(d, "thumb.png"))
                    s["stage"] = "done"; s["refine_credits"] = t.get("consumed_credits")
                    json.dump({"id": jid, "preview": s["preview"], "refine": t["id"], "prompt": s["job"]["prompt"],
                               "texture_prompt": s["job"].get("texture_prompt"), "polycount": s["job"].get("polycount"),
                               "credits": (s.get("preview_credits") or 0) + (t.get("consumed_credits") or 0),
                               "glb_bytes": size}, open(os.path.join(d, "task.json"), "w", encoding="utf-8"),
                              ensure_ascii=False, indent=1)
                    print(jid, "ГОТОВО", size, "байт")
                except Exception as e:
                    s["stage"] = "error"; s["error"] = str(e); print(jid, "ОШИБКА скачивания", e)
    report = {jid: {k: v for k, v in s.items() if k != "job"} for jid, s in state.items()}
    json.dump(report, open(os.path.join(out, "report-" + time.strftime("%Y%m%d-%H%M%S") + ".json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)
    bad = [jid for jid, s in state.items() if s["stage"] != "done"]
    print("итог: готово", len(state) - len(bad), "из", len(state), "; не готово:", bad)

def poll(url, key, what, timeout=40 * 60):
    end = time.time() + timeout
    while time.time() < end:
        time.sleep(12)
        try: t = call("GET", url, key=key)
        except Exception as e: print(what, "опрос:", e); continue
        if t.get("status") == "SUCCEEDED": return t
        if t.get("status") in ("FAILED", "CANCELED"): raise RuntimeError(what + " " + json.dumps(t.get("task_error")))
    raise RuntimeError(what + " не успел")

def run_concept(jobs, out, base):
    """Концепт по образцу (Image to Image: картинки удачных моделей + описание класса) → Image to 3D с текстурой.
    Задание: {"id", "concept": true, "account": 2, "refs": ["refs/brig.png"], "image_prompt": "...", "polycount": 40000,
    "image_model": "nano-banana"}. Задания идут по очереди в своих потоках."""
    import threading
    def one(j):
        jid = j["id"]; key = KEYS.get(int(j.get("account", 1))) or KEY; d = os.path.join(out, jid); os.makedirs(d, exist_ok=True)
        info = {"id": jid, "account": j.get("account", 1), "image_prompt": j["image_prompt"]}
        try:
            refs = [data_uri(os.path.join(base, r)) for r in j.get("refs", [])]
            tid = call("POST", I2I, {"ai_model": j.get("image_model", "nano-banana"), "prompt": j["image_prompt"][:2000],
                                     "reference_image_urls": refs, "aspect_ratio": "4:3", "remove_background": True}, key=key)["result"]
            print(jid, "концепт", tid)
            t = poll(I2I + "/" + tid, key, jid + " концепт")
            fetch(t["image_urls"][0], os.path.join(d, "concept.png")); info["concept"] = tid; info["concept_credits"] = t.get("consumed_credits")
            print(jid, "КОНЦЕПТ ГОТОВ", t.get("consumed_credits"))
            if j.get("concept_only"):
                info["stage"] = "concept"; return
            body = {"input_task_id": tid, "ai_model": j.get("ai_model", "latest"), "should_texture": True, "enable_pbr": True,
                    "should_remesh": True, "topology": "triangle", "target_polycount": int(j.get("polycount", 40000)),
                    "target_formats": ["glb"]}
            mid = call("POST", I23, body, key=key)["result"]; print(jid, "3D", mid)
            t = poll(I23 + "/" + mid, key, jid + " 3D")
            size = fetch(t["model_urls"]["glb"], os.path.join(d, "model.glb"))
            if t.get("thumbnail_url"): fetch(t["thumbnail_url"], os.path.join(d, "thumb.png"))
            info.update({"model": mid, "model_credits": t.get("consumed_credits"), "glb_bytes": size, "stage": "done"})
            print(jid, "ГОТОВО", size, "байт, кредиты", t.get("consumed_credits"))
        except Exception as e:
            info["stage"] = "error"; info["error"] = str(e); print(jid, "ОШИБКА", e)
        finally:
            json.dump(info, open(os.path.join(d, "task.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    ths = [threading.Thread(target=one, args=(j,)) for j in jobs]
    for t in ths: t.start()
    for t in ths: t.join()

if __name__ == "__main__":
    main()
