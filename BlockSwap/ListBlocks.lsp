;;; ListBlocks.lsp
;;; Lists every block in the current drawing, with how many times each one
;;; is used, and saves the list to a .txt file next to the drawing.
;;;
;;; Load: APPLOAD (or drag this file into AutoCAD), then type LISTBLOCKS.
;;;
;;; The bottom of the file has the names ready to paste into the
;;; BS-DELETE / BS-COPY lines of BlockSwap.scr.

(vl-load-com)

(defun LB:EffName (obj)
  (if (vlax-property-available-p obj 'EffectiveName)
    (vla-get-EffectiveName obj)
    (vla-get-Name obj)
  )
)

(defun LB:PadRight (s n)
  (while (< (strlen s) n) (setq s (strcat s " ")))
  s
)

(defun c:LISTBLOCKS (/ doc counts ss i name pair names xrefs file f width paste line)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  ;; Count references in model space and every layout (dynamic blocks by real name).
  (if (setq ss (ssget "_X" '((0 . "INSERT"))))
    (repeat (setq i (sslength ss))
      (setq name (strcase (LB:EffName (vlax-ename->vla-object (ssname ss (setq i (1- i)))))))
      (if (setq pair (assoc name counts))
        (setq counts (subst (cons name (1+ (cdr pair))) pair counts))
        (setq counts (cons (cons name 1) counts))
      )
    )
  )

  ;; Block definitions, skipping layouts and anonymous (*U, *D...) blocks.
  (vlax-for blk (vla-get-Blocks doc)
    (setq name (vla-get-Name blk))
    (cond
      ((= (vla-get-IsLayout blk) :vlax-true))
      ((= (substr name 1 1) "*"))
      ((= (vla-get-IsXRef blk) :vlax-true) (setq xrefs (cons name xrefs)))
      (t (setq names (cons name names)))
    )
  )
  (setq names (vl-sort names '(lambda (a b) (< (strcase a) (strcase b))))
        xrefs (vl-sort xrefs '(lambda (a b) (< (strcase a) (strcase b))))
        width 10)
  (foreach n names (setq width (max width (+ 2 (strlen n)))))

  (setq file (strcat (getvar "DWGPREFIX") (vl-filename-base (getvar "DWGNAME")) "_Blocks.txt"))
  (if (not (setq f (open file "w")))
    (progn (princ (strcat "\nCould not write " file)) (setq f nil))
  )

  (defun LB:Out (s) (princ (strcat "\n" s)) (if f (write-line s f)))

  (LB:Out (strcat "Blocks in " (getvar "DWGPREFIX") (getvar "DWGNAME")))
  (LB:Out (strcat (itoa (length names)) " block(s)  (Used = placed in model space / layouts; 0 = unused or only nested)"))
  (LB:Out "")
  (LB:Out (strcat (LB:PadRight "Block name" width) "Used"))
  (LB:Out (strcat (LB:PadRight "----------" width) "----"))
  (foreach n names
    (LB:Out (strcat (LB:PadRight n width)
                    (itoa (cond ((cdr (assoc (strcase n) counts))) (0)))))
  )
  (if xrefs
    (progn
      (LB:Out "")
      (LB:Out "Xrefs (not blocks, listed for info):")
      (foreach n xrefs (LB:Out (strcat "  " n)))
    )
  )

  ;; Ready-to-paste list for BlockSwap.scr
  (setq paste "")
  (foreach n names (setq paste (strcat paste " \"" n "\"")))
  (LB:Out "")
  (LB:Out "Paste-ready (delete the names you don't want):")
  (LB:Out (strcat "'(" (vl-string-left-trim " " paste) ")"))

  (if f
    (progn
      (close f)
      (princ (strcat "\n\nSaved to: " file))
      (startapp "notepad" file)
    )
  )
  (princ)
)

(princ "\nListBlocks loaded. Type LISTBLOCKS.")
(princ)
