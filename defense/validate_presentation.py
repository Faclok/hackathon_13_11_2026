"""Check the PDF, editable deck and layout model after rebuilding."""
from pathlib import Path
import json
import sys

from pypdf import PdfReader
from pptx import Presentation

sys.stdout.reconfigure(encoding="utf-8")
root = Path(__file__).resolve().parent
scenes = json.loads((root / "slides.json").read_text(encoding="utf-8"))
reader = PdfReader(root / "Final_defense.pdf")
deck = Presentation(root / "Final_defense.pptx")
text = "\n".join(page.extract_text() for page in reader.pages)

assert len(reader.pages) == len(deck.slides) == len(scenes) == 6
assert "Башперевод" not in text
for term in ("Дуэли", "Культурные квесты", "Школьные лиги", "Кабинет ученика", "Кабинет преподавателя", "Кабинет эксперта", "Telegram-бот", "250 заданий"):
    assert term in text, term
assert all(slide.notes_slide.notes_text_frame.text == scene["notes"] for slide, scene in zip(deck.slides, scenes))
speech_five = deck.slides[4].notes_slide.notes_text_frame.text
assert all(term in speech_five for term in ("ученика", "преподавателя", "эксперта", "Telegram-бот"))
assert all(term not in speech_five for term in ("PostgreSQL", "JSON", "CSV"))

for number, scene in enumerate(scenes, 1):
    for element in scene["elements"]:
        if element["kind"] not in ("rect", "image", "text"):
            continue
        x, y, w, h = (element[key] for key in ("x", "y", "w", "h"))
        assert x >= 0 and y >= 0 and x + w <= 1600.1 and y + h <= 900.1, (number, element)
        if element["kind"] == "image":
            assert (root / element["file"]).exists()

embedded = set()
for page in reader.pages:
    for entry in page["/Resources"]["/Font"].get_object().values():
        font = entry.get_object()
        if "Inter" in str(font.get("/BaseFont")):
            descriptor = font["/FontDescriptor"].get_object()
            assert "/FontFile2" in descriptor
            embedded.add(str(font["/BaseFont"]))
assert len(embedded) >= 3

print("PASS: six slides, required topics, speaker notes, valid asset paths and layout bounds; Inter fonts embedded in PDF.")
