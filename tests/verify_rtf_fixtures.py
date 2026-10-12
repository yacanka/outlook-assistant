"""macOS-only independent RTF reader checks; does not replace Windows RichEdit/Outlook QA."""
import subprocess
import sys
from html.parser import HTMLParser
from pathlib import Path


class ParsedHtml(HTMLParser):
    def __init__(self, html):
        super().__init__()
        self.tags = []
        self.text = []
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        self.tags.append(tag)

    def handle_data(self, data):
        self.text.append(data)


def read_fixture(folder, name):
    result = subprocess.run(
        ["textutil", "-convert", "html", "-stdout", str(folder / (name + ".rtf"))],
        check=True, capture_output=True, text=True,
    )
    parsed = ParsedHtml(result.stdout)
    assert "html" in parsed.tags, f"RTF reader rejected {name}: {result.stderr}"
    return parsed, result.stdout


def main():
    folder = Path(sys.argv[1])
    inline, _ = read_fixture(folder, "inline")
    assert inline.tags.count("b") >= 2 and "i" in inline.tags
    assert "Alt satır 😀" in "".join(inline.text)
    lists, _ = read_fixture(folder, "lists")
    assert lists.tags.count("li") == 3 and "ol" in lists.tags and "ul" in lists.tags
    table, _ = read_fixture(folder, "table")
    assert table.tags.count("tr") == 2 and table.tags.count("td") == 4
    uneven, _ = read_fixture(folder, "uneven")
    assert uneven.tags.count("tr") == 3 and uneven.tags.count("td") == 9
    assert "Z" in "".join(uneven.text)
    quote, html = read_fixture(folder, "quote")
    assert "Alıntı" in "".join(quote.text) and "18.0px" in html
    links, _ = read_fixture(folder, "links")
    assert links.tags.count("a") == 1 and "img" not in links.tags
    code, _ = read_fixture(folder, "code")
    assert "C:\\temp" in "".join(code.text) and "**literal**" in "".join(code.text)
    print("Independent RTF checks passed (native lists, quote indent, tables, emphasis, Unicode, links, code).")


if __name__ == "__main__":
    main()
