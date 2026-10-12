;;; CloneBlock.lsp
;;; Makes exact 1:1 copies of a block definition (same geometry, same
;;; attributes) under a new name: the block's name with the next free number,
;;; counting from 1.  e.g. MY_BLOCK1..MY_BLOCK9 exist -> new MY_BLOCK10
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

(defun c:CLONEBLOCK (/ doc sel name src parts base digits width qty i newname made)
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

      (initget 6)
      (setq qty (getint "\nHow many new blocks <1>: "))
      (if (not qty) (setq qty 1))

      ;; Count up from 1 and use the first numbers that aren't taken yet.
      (vla-StartUndoMark doc)
      (setq i 1 made "")
      (while (> qty 0)
        (setq newname (strcat base (CB:Pad i width)))
        (if (not (tblsearch "BLOCK" newname))
          (progn
            (CB:Clone doc src newname)
            (setq made (strcat made "\n  " newname) qty (1- qty))
          )
        )
        (setq i (1+ i))
      )
      (vla-EndUndoMark doc)
      (princ (strcat "\nCreated (exact copies of " name "):" made))
    )
  )
  (princ)
)

(princ "\nCloneBlock loaded. Type CLONEBLOCK.")
(princ)
