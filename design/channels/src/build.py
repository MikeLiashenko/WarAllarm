"""Иконки каналов WarAlarm для Києва и Харкова.

Стиль — как у иконки приложения (app/lib/widgets/brand_icon.dart): прапор,
чорний щит із білим кантом, золотий знак. Замість тризуба — герб міста:
  Київ   — архістратиг Михаїл з мечем і щитом;
  Харків — ріг достатку і кадуцей навхрест.

Запуск (нужны python3 и node с пакетом playwright):
    cd design/channels/src && python3 build.py
Результат — в design/channels/:
    <city>_avatar.png  1024×1024, квадрат на весь кадр — аватар Telegram-канала
                       (Telegram сам обрезает в круг, щит в круг влезает).
    <city>_logo.png    1024×1024, скруглённый квадрат с прозрачными углами —
                       как app/assets/icon/waralarm_1024.png (логотип на картинках).
    <city>*.svg        вектор-исходники.
    *_heraldic_*       альтернатива: щит в геральдическом цвете города
                       (лазурь у Києва, зелень у Харкова).
    preview.png        как это выглядит в Telegram рядом с текущим WarAlarm.png.
"""
import os, subprocess, tpl, kyiv, kharkiv

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.dirname(HERE)
FIELDS = {
    'black':   ('#1A1F29', '#05070B'),   # как в brand_icon.dart
    'kyiv':    ('#2F6FD0', '#123E8C'),   # лазурь
    'kharkiv': ('#1F9150', '#0B5A2C'),   # зелень
}

def render(svg, png, px=1024):
    subprocess.run(['node', os.path.join(HERE, 'render.js'), svg, png, str(px)], check=True)

def main():
    for city, mod in (('kyiv', kyiv), ('kharkiv', kharkiv)):
        for var in ('', '_heraldic'):
            field = FIELDS[city if var else 'black']
            for kind, rounded in (('avatar', False), ('logo', True)):
                base = os.path.join(OUT, f'{city}{var}_{kind}')
                with open(base + '.svg', 'w') as fh:
                    fh.write(tpl.icon(mod.emblem(), rounded=rounded, field=field))
                render(base + '.svg', base + '.png')
                print(os.path.relpath(base + '.png', OUT))
    root = os.path.dirname(os.path.dirname(OUT))
    p = lambda n: os.path.join(OUT, n)
    subprocess.run(['node', os.path.join(HERE, 'preview.js'), p('preview.png'),
                    f'WarAlarm|Ракета на Київ…={os.path.join(root, "WarAlarm.png")}',
                    f'WarAlarm Київ|Балістика на Київ…={p("kyiv_avatar.png")}',
                    f'WarAlarm Харків|Шахед на Харків…={p("kharkiv_avatar.png")}',
                    f'Київ · лазур|Балістика на Київ…={p("kyiv_heraldic_avatar.png")}',
                    f'Харків · зелень|Шахед на Харків…={p("kharkiv_heraldic_avatar.png")}'], check=True)
    print('preview.png')

if __name__ == '__main__':
    main()
