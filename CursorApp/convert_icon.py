import os
import struct
from PIL import Image

src_img_path = r"C:\Users\user\.cursor\projects\c-Users-user-Desktop-Hololive-Mouse-cursor-20260831T004121Z-1-001\assets\c__Users_user_AppData_Roaming_Cursor_User_workspaceStorage_4a012ecb8aff41db51ba5c1052067928_images_OIP__2_-977ceb05-cf97-4544-8a55-630c2ab68331.webp"
dst_ico_path = r"C:\Users\user\Desktop\CursorApp\CursorApp\app.ico"

img = Image.open(src_img_path).convert("RGBA")

# Focus/crop around the avatar character head/face for best icon view, or square crop centered
w, h = img.size
# Let's do a square crop centered horizontally and focused slightly towards the upper half (head/face)
crop_size = min(w, h)
left = (w - crop_size) // 2
top = int(h * 0.05)
if top + crop_size > h:
    top = h - crop_size

cropped = img.crop((left, top, left + crop_size, top + crop_size))

# Save multi-size icon
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
cropped.save(dst_ico_path, format="ICO", sizes=sizes)
print("Successfully generated app.ico at", dst_ico_path)
