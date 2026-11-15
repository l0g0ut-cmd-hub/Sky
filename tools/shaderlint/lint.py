#!/usr/bin/env python3
"""Быстрая проверка шейдеров без Unity: имена переменных, совпадающие с зарезервированными словами HLSL/GLSL
(15.11 сборки 143–144 упали на «float line» в ZoneMist.shader). Код 1 — есть проблемы."""
import re, sys, glob, os
RESERVED = set("""line point triangle lineadj triangleadj sample centroid linear noperspective nointerpolation precise
packoffset register shared groupshared uniform static extern inline volatile string texture sampler buffer
input output inout in out pass technique compile matrix vector half fixed snorm unorm export typedef namespace
class interface switch case default goto filter active varying attribute common partition superp smooth flat
patch subroutine resource coherent restrict readonly writeonly""".split())
root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '../../Assets')
bad = 0
for f in glob.glob(os.path.join(root, '**/*.shader'), recursive=True) + glob.glob(os.path.join(root, '**/*.cginc'), recursive=True):
    src = open(f, encoding='utf-8', errors='replace').read()
    for m in re.finditer(r'\b(?:float[234]?|half[234]?|fixed[234]?|int|bool)\s+([A-Za-z_]\w*)\s*[=;,)]', src):
        if m.group(1) in RESERVED:
            ln = src.count('\n', 0, m.start()) + 1
            print(f'{os.path.relpath(f)}:{ln}: имя «{m.group(1)}» зарезервировано в HLSL/GLSL'); bad += 1
print(f'шейдеры: проблем {bad}')
sys.exit(1 if bad else 0)
