import os
from PIL import Image, ImageDraw, ImageFont
from ios import Net, Router, session

HERE = os.path.dirname(os.path.abspath(__file__))
IMG = os.path.join(HERE, 'img'); os.makedirs(IMG, exist_ok=True)
FONT = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf', 15)
FB = ImageFont.truetype('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf', 14)
LH = 19

def term(path, title, lines):
    w = 900
    h = 34 + LH * len(lines) + 20
    im = Image.new('RGB', (w, h), (18, 18, 18))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, w, 28], fill=(60, 60, 64))
    d.text((10, 6), title, font=FB, fill=(230, 230, 230))
    y = 36
    for ln in lines:
        d.text((10, y), ln, font=FONT, fill=(215, 215, 215)); y += LH
    im.save(path)

net = Net()
r1 = Router(net, '0001.4210.a001')
r2 = Router(net, '0060.2f83.b001')

def seg(router, cmds, cap=None):
    """Выполнить команды, вернуть транскрипт; длинный вывод режется постранично (--More--)."""
    out = []
    for c in cmds:
        t = session_one(router, c)
        if cap and len(t) > cap + 1:
            t = t[:cap + 1] + [' --More--']
        out += t
    out.append(router.prompt())
    return out

def session_one(router, c):
    t = session(router, [c])
    return t[:-1]          # без хвостового приглашения

def fig(n, router, title, cmds, cap=None):
    lines = seg(router, cmds, cap)
    term(os.path.join(IMG, 'fig%d.png' % n), title, lines)

T1 = 'Router1 – CLI (эмуляция CLI Cisco IOS)'
T2 = 'Router2 – CLI (эмуляция CLI Cisco IOS)'

# Рис.2
fig(2, r1, T1, ['?', 'enable', '?', 'config terminal', 'hostname Router1', 'enable password parol'], cap=5)
# Рис.3: проверка пароля
fig(3, r1, T1, ['exit', 'disable', 'en'])
# Рис.4
r1.run('disable')
fig(4, r1, T1, ['show ?', 'show version', 'show clock', 'show flash', 'show history'], cap=9)
# Рис.5
fig(5, r1, T1, ['show hosts', 'show sessions', 'show users', 'show controllers'], cap=9)
fig(55, r1, T1, ['show interfaces', 'show terminal'], cap=9)
# Рис.6
fig(6, r1, T1, ['en', 'show ?', 'write memory', 'show running-config'], cap=18)
fig(66, r1, T1, ['show protocols'])
# Рис.7
fig(7, r1, T1, ['conf t', 'interface FastEthernet0/0', '?', 'exit', 'int fa0/0', 'no shutdown',
                'description Ethernet interface on Router 1', 'end', 'show interface'], cap=6)
# Рис.8 Router2
fig(8, r2, T2, ['enable', 'conf t', 'hostname Router2', 'interface fa0/0', 'no shutdown'])
# Рис.9
fig(9, r1, T1, ['show controllers fa0/1', 'conf t', 'int fa0/1', 'clock rate ?', 'clock rate 64000', 'no shut'])

# Рис.1: схема
w, h = 900, 220
im = Image.new('RGB', (w, h), (250, 250, 250)); d = ImageDraw.Draw(im)
for x, name in ((170, 'Router1'), (730, 'Router2')):
    d.ellipse([x - 55, 60, x + 55, 130], outline=(40, 90, 160), width=3, fill=(220, 232, 248))
    d.text((x - 28, 88), '1841', font=FB, fill=(30, 30, 30))
    d.text((x - 32, 140), name, font=FB, fill=(30, 30, 30))
d.line([225, 95, 675, 95], fill=(20, 20, 20), width=3)
d.text((380, 66), 'Copper Cross-Over', font=FONT, fill=(30, 30, 30))
d.text((230, 100), 'Fa0/0', font=FONT, fill=(30, 30, 30)); d.text((620, 100), 'Fa0/0', font=FONT, fill=(30, 30, 30))
im.save(os.path.join(IMG, 'fig1.png'))
print(open(os.path.join(IMG, 'x'), 'w').write('') if False else 'rendered')
