;;; ---------------------------------------------------------------------------
;;; PlotAllLayouts.lsp
;;;
;;; Plots every paper-space layout (never Model space) of the current drawing.
;;;
;;; Works in:
;;;   - Civil 3D 2026 / AutoCAD 2026 (type PLOTALL)
;;;   - The AutoCAD Core Console (accoreconsole.exe), which is what
;;;     Plot-Folder.ps1 and the Autodesk Batch Save Utility run scripts in.
;;;
;;; Only core AutoLISP is used (no vla-*/vlax-* calls) because the Core
;;; Console has no ActiveX support.
;;;
;;; Each layout is plotted with its OWN page setup (device, paper size,
;;; scale, plot style...) unless *plotall-pagesetup* is set.
;;;   - File devices (DWG To PDF.pc3, AutoCAD PDF*.pc3, DWF6, DWFx, PNG/JPG
;;;     .pc3) write  <OutputFolder>\<DrawingName>-<LayoutName>.<ext>
;;;   - Real printers/plotters are sent the job directly.
;;;
;;; Optional settings - set these globals BEFORE calling (plotall:run):
;;;   *plotall-outdir*        Output folder for PDF/DWF files.
;;;                           nil = "<drawing folder>\Plots"
;;;   *plotall-pagesetup*     Named page setup to apply to EVERY layout.
;;;                           nil = use each layout's own page setup.
;;;   *plotall-pagesetup-dwg* DWG/DWT to import *plotall-pagesetup* from when
;;;                           the drawing doesn't already contain it.
;;;   *plotall-include-empty* T = also plot layouts that have nothing on them
;;;                           (default: empty layouts are skipped).
;;;   *plotall-log*           CSV file to append results to. nil = no log.
;;; ---------------------------------------------------------------------------

;; Returns ((name . ename) ...) for a named-object dictionary, in stored order.
(defun plotall:dict-entries (dictname / name out)
  (foreach pr (dictsearch (namedobjdict) dictname)
    (cond ((= (car pr) 3) (setq name (cdr pr)))
          ((and name (member (car pr) '(350 360)))
           (setq out  (cons (cons name (cdr pr)) out)
                 name nil))))
  (reverse out))

;; Paper-space layouts in tab order: ((layoutName deviceName) ...)
(defun plotall:layouts (/ ed out)
  (foreach pr (plotall:dict-entries "ACAD_LAYOUT")
    (if (/= (strcase (car pr)) "MODEL")
      (progn
        (setq ed (entget (cdr pr)))
        (setq out (cons (list (cond ((cdr (assoc 71 ed))) (0))   ; tab order
                              (car pr)                           ; layout name
                              (cond ((cdr (assoc 2 ed))) ("")))  ; plot device
                        out)))))
  (mapcar 'cdr (vl-sort out '(lambda (a b) (< (car a) (car b))))))

(defun plotall:find-pagesetup (name / hit)
  (foreach pr (plotall:dict-entries "ACAD_PLOTSETTINGS")
    (if (= (strcase (car pr)) (strcase name)) (setq hit (cdr pr))))
  hit)

(defun plotall:import-pagesetup (name src)
  (if (findfile src)
    (progn
      (command "_.-PSETUPIN" src name)
      (if (> (getvar "CMDACTIVE") 0) (command)))
    (princ (strcat "\nPLOTALL: page setup source not found: " src))))

(defun plotall:replace-all (str old new / pos)
  (setq pos 0)
  (while (setq pos (vl-string-search old str pos))
    (setq str (strcat (substr str 1 pos) new (substr str (+ pos 1 (strlen old))))
          pos (+ pos (strlen new))))
  str)

;; Prefix every char found in CHARS with PREFIX, or replace it with REPL.
(defun plotall:map-chars (s chars prefix repl / out i ch)
  (setq out "" i 1)
  (repeat (strlen s)
    (setq ch  (substr s i 1)
          out (strcat out
                      (cond ((not (vl-string-search ch chars)) ch)
                            (repl repl)
                            (T (strcat prefix ch))))
          i   (1+ i)))
  out)

(defun plotall:safe-filename (s) (plotall:map-chars s "\\/:*?\"<>|" nil "_"))
(defun plotall:escape-wc (s) (plotall:map-chars s "#@.*?~[]-,`" "`" nil))

;; T when a layout holds nothing but its own paper-space viewport.
(defun plotall:layout-empty-p (name / ss i ed n)
  (setq n 0 i 0)
  (if (setq ss (ssget "_X" (list (cons 410 (plotall:escape-wc name)))))
    (repeat (sslength ss)
      (setq ed (entget (ssname ss i))
            i  (1+ i))
      (if (not (and (= (cdr (assoc 0 ed)) "VIEWPORT") (= (cdr (assoc 69 ed)) 1)))
        (setq n (1+ n)))))
  (= n 0))

;; File extension for devices that write files, nil for real printers.
(defun plotall:file-ext (dev / u)
  (setq u (strcase dev))
  (cond ((wcmatch u "*DWFX*")                ".dwfx")
        ((wcmatch u "*DWF*")                 ".dwf")
        ((wcmatch u "*PDF*.PC3")             ".pdf")
        ((wcmatch u "*PNG*.PC3")             ".png")
        ((wcmatch u "*JPG*.PC3,*JPEG*.PC3")  ".jpg")
        ((wcmatch u "*TIF*.PC3")             ".tif")))

(defun plotall:mkdir (dir / parent)
  (setq dir (vl-string-right-trim "\\" dir))
  (if (not (vl-file-directory-p dir))
    (progn
      (setq parent (vl-filename-directory dir))
      (if (and parent (/= parent "") (/= parent dir)) (plotall:mkdir parent))
      (vl-mkdir dir)))
  (vl-file-directory-p dir))

(defun plotall:csv (s) (strcat "\"" (plotall:replace-all (if s s "") "\"" "\"\"") "\""))

(defun plotall:log (dwg lay dev status out / f)
  (princ (strcat "\nPLOTALL: [" lay "] " status (if out (strcat " -> " out) "")))
  (if (and *plotall-log* (setq f (open *plotall-log* "a")))
    (progn
      (write-line (strcat (plotall:csv dwg) "," (plotall:csv lay) "," (plotall:csv dev) ","
                          (plotall:csv status) "," (plotall:csv out))
                  f)
      (close f))))

;; -PLOT, non-detailed: uses the layout's (or the named) page setup as-is.
;; File devices prompt for a file name, printers ask "Write the plot to a file?".
(defun plotall:plot-layout (name ps out)
  (command "_.-PLOT" "_N" name (if ps ps "") "" (if out out "_N") "_N" "_Y")
  (cond ((> (getvar "CMDACTIVE") 0)
         (command)
         "FAILED (unexpected plot prompt)")
        ((not out) "SENT TO PLOTTER")
        ((findfile out) "PLOTTED")
        (T "FAILED (no output file created)")))

(defun plotall:restore (vars)
  (foreach v vars (if (cdr v) (setvar (car v) (cdr v)))))

(defun plotall:run (/ *error* oldvars dwg outdir ps psdev ps-ok name dev ext out status n-ok n-skip n-fail)
  (defun *error* (msg)
    (plotall:restore oldvars)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nPLOTALL error: " msg)))
    (princ))
  (setq oldvars (mapcar '(lambda (v) (cons v (getvar v)))
                        '("FILEDIA" "CMDECHO" "BACKGROUNDPLOT")))
  (foreach v oldvars (if (cdr v) (setvar (car v) 0)))

  (setq dwg    (strcat (getvar "DWGPREFIX") (getvar "DWGNAME"))
        outdir (if (and *plotall-outdir* (/= *plotall-outdir* ""))
                 (vl-string-translate "/" "\\" *plotall-outdir*)
                 (strcat (getvar "DWGPREFIX") "Plots"))
        n-ok 0 n-skip 0 n-fail 0
        ps-ok T)
  (if (/= (substr outdir (strlen outdir)) "\\") (setq outdir (strcat outdir "\\")))

  ;; Optional: one named page setup for every layout
  (if (and *plotall-pagesetup* (/= *plotall-pagesetup* ""))
    (progn
      (setq ps *plotall-pagesetup*)
      (if (and (not (plotall:find-pagesetup ps)) *plotall-pagesetup-dwg*)
        (plotall:import-pagesetup ps *plotall-pagesetup-dwg*))
      (if (plotall:find-pagesetup ps)
        (setq psdev (cond ((cdr (assoc 2 (entget (plotall:find-pagesetup ps))))) ("")))
        (progn
          (setq ps-ok nil)
          (plotall:log dwg "*" "" (strcat "FAILED (page setup '" ps "' not found)") nil)))))

  (if ps-ok
    (foreach lay (plotall:layouts)
      (setq name (car lay)
            dev  (if ps psdev (cadr lay))
            ext  (plotall:file-ext dev)
            out  nil)
      (setq status
        (cond
          ((and (not *plotall-include-empty*) (plotall:layout-empty-p name))
           "SKIPPED (empty layout)")
          ((member (strcase dev) '("" "NONE" "NONE_DEVICE"))
           "SKIPPED (page setup has no plotter - set one, e.g. DWG To PDF.pc3)")
          ((and (not ext) (wcmatch (strcase dev) "*PDF*,*XPS*,*ONENOTE*"))
           "SKIPPED (Windows PDF/XPS printer opens a Save dialog - use DWG To PDF.pc3)")
          ((and ext (not (plotall:mkdir outdir)))
           (strcat "FAILED (cannot create output folder " outdir ")"))
          (T
           (if ext
             (progn
               (setq out (strcat outdir
                                 (plotall:safe-filename (vl-filename-base (getvar "DWGNAME")))
                                 "-" (plotall:safe-filename name) ext))
               (if (findfile out) (vl-file-delete out))))
           (plotall:plot-layout name ps out))))
      (cond ((wcmatch status "SKIPPED*") (setq n-skip (1+ n-skip)))
            ((wcmatch status "FAILED*")  (setq n-fail (1+ n-fail)))
            (T                           (setq n-ok   (1+ n-ok))))
      (plotall:log dwg name dev status out)))

  (plotall:restore oldvars)
  (princ (strcat "\nPLOTALL: " (getvar "DWGNAME") " - "
                 (itoa n-ok) " plotted, " (itoa n-skip) " skipped, " (itoa n-fail) " failed."))
  (princ))

;; Interactive: plot all layouts of the open drawing.
(defun c:PLOTALL () (plotall:run) (princ))

(princ "\nPlotAllLayouts loaded. Type PLOTALL to plot every layout of this drawing.")
(princ)
