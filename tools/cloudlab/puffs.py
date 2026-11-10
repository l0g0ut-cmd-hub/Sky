import numpy as np
def make_tower(seed, R=190., H=520.):
    """Набор сфер одной кучевой башни: (x, y, z, r). Основание широкое и плоское, выше — сужение, наверху 3–5 голов."""
    rng=np.random.default_rng(seed); S=[]
    for i in range(7):                      # основание
        a=i/7*2*np.pi+rng.uniform(-.3,.3); d=R*rng.uniform(0.35,0.7)
        S.append((np.cos(a)*d, H*0.16, np.sin(a)*d, R*rng.uniform(0.42,0.55)))
    S.append((0,H*0.2,0,R*0.6))
    for i in range(6):                      # средний ярус
        a=i/6*2*np.pi+rng.uniform(-.4,.4); d=R*rng.uniform(0.2,0.45)
        S.append((np.cos(a)*d, H*rng.uniform(0.38,0.52), np.sin(a)*d, R*rng.uniform(0.36,0.46)))
    S.append((rng.uniform(-.1,.1)*R,H*0.5,0,R*0.48))
    nh=rng.integers(3,6)
    for i in range(nh):                     # головы
        a=rng.uniform(0,2*np.pi); d=R*rng.uniform(0.05,0.32)
        S.append((np.cos(a)*d, H*rng.uniform(0.66,0.86), np.sin(a)*d, R*rng.uniform(0.26,0.36)))
    for i in range(6):                      # мелкие наплывы по бокам
        a=rng.uniform(0,2*np.pi); y=H*rng.uniform(0.25,0.7); d=R*(0.75-0.5*y/H)
        S.append((np.cos(a)*d, y, np.sin(a)*d, R*rng.uniform(0.16,0.24)))
    return np.array(S,np.float32)
