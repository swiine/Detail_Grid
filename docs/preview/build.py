# Builds "../ET Wall Tool preview.html" from page.html + calc.js + section.js + report.js:
#   python3 build.py
import os
d = os.path.dirname(os.path.abspath(__file__))
r = lambda n: open(os.path.join(d, n), encoding="utf-8").read()
p = r("page.html").replace("CALC_JS", r("calc.js") + "\n" + r("section.js"))
i = p.rindex("</script>")
p = p[:i + 9] + "\n<script>\n" + r("report.js") + "</script>\n" + p[i + 9:]
open(os.path.join(d, "..", "ET Wall Tool preview.html"), "w", encoding="utf-8").write(p)
