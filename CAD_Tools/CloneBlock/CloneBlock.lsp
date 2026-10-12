;;; CloneBlock.lsp
;;; Makes exact 1:1 copies of a block definition under new numbered names.
;;; e.g. MY_BLOCK9  ->  MY_BLOCK10, MY_BLOCK11 ... MY_BLOCK20
;;;
;;; Load: APPLOAD (or drag this file into AutoCAD), then type CLONEBLOCK.
;;; The new blocks are created in the drawing's block list (not placed).
;;; Use INSERT to place them.

(vl-load-com)

;; Splits "NAME_Text9" into ("NAME_Text" "9"). No trailing number -> ("NAME" "").
(defun CB:SplitNum (name / i)
  (setq i (strlen name))
  (while (and (> i 0) (wcmatch (substr name i 1) "#")) (setq i (1- i)))
  (list (substr name 1 i) (substr name (1+ i)))
)

;; Keeps leading zeros if the original number had them (09 -> 10, 11 ...; 009 -> 010).
(defun CB:Pad (n width / s)
  (setq s (itoa n))
  (while (< (strlen s) width) (setq s (strcat "0" s)))
  s
)

(defun CB:Clone (doc src newname / newblk objs arr)
  (setq newblk (vla-Add (vla-get-Blocks doc) (vla-get-Origin src) newname))
  (vlax-for e src (setq objs (cons e objs)))
  (if objs
    (progn
      (setq arr (vlax-make-safearray vlax-vbObject (cons 0 (1- (length objs)))))
      (vlax-safearray-fill arr (reverse objs))
      (vla-CopyObjects doc arr newblk)
    )
  )
  (foreach prop '(Units Explodable BlockScaling Comments)
    (vl-catch-all-apply 'vlax-put-property
      (list newblk prop (vlax-get-property src prop)))
  )
  newblk
)

(defun c:CLONEBLOCK (/ doc sel name src parts base digits width start end i newname made skipped)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  ;; Pick a block, or press Enter to type the name.
  (setq sel (entsel "\nSelect the block to copy <Enter to type its name>: "))
  (if (and sel (= (cdr (assoc 0 (entget (car sel)))) "INSERT"))
    (setq name (vla-get-EffectiveName (vlax-ename->vla-object (car sel))))
    (setq name (getstring T "\nBlock name to copy: "))
  )

  (cond
    ((or (not name) (= name "") (not (tblsearch "BLOCK" name)))
      (princ (strcat "\nNo block called \"" (if name name "") "\" in this drawing."))
    )
    (t
      (setq src (vla-Item (vla-get-Blocks doc) name))
      (if (= (vla-get-IsDynamicBlock src) :vlax-true)
        (princ "\nNOTE: this is a dynamic block - geometry and attributes are copied, but its grips/parameters will not be.")
      )
      (setq parts  (CB:SplitNum name)
            base   (car parts)
            digits (cadr parts)
            width  (if (and (> (strlen digits) 1) (= (substr digits 1 1) "0")) (strlen digits) 0))
      (if (= digits "")
        (princ (strcat "\n\"" name "\" has no number on the end - new blocks will be " name "1, " name "2 ..."))
      )
      (princ (strcat "\nCopying \"" name "\"  (new names: " base "<number>)"))

      (initget 4)
      (setq start (getint (strcat "\nFirst new number <" (itoa (1+ (if (= digits "") 0 (atoi digits)))) ">: ")))
      (if (not start) (setq start (1+ (if (= digits "") 0 (atoi digits)))))
      (initget 5)
      (setq end (getint "\nLast new number: "))

      (if (< end start)
        (princ "\nLast number is smaller than the first - nothing done.")
        (progn
          (vla-StartUndoMark doc)
          (setq i start made 0 skipped "")
          (while (<= i end)
            (setq newname (strcat base (CB:Pad i width)))
            (if (tblsearch "BLOCK" newname)
              (setq skipped (strcat skipped " " newname))
              (progn (CB:Clone doc src newname) (setq made (1+ made)))
            )
            (setq i (1+ i))
          )
          (vla-EndUndoMark doc)
          (princ (strcat "\nCreated " (itoa made) " block(s): "
                         base (CB:Pad start width) " to " base (CB:Pad end width)))
          (if (/= skipped "")
            (princ (strcat "\nSkipped (already exist, left untouched):" skipped))
          )
        )
      )
    )
  )
  (princ)
)

(princ "\nCloneBlock loaded. Type CLONEBLOCK.")
(princ)
