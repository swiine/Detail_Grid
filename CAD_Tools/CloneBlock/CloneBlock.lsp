;;; CloneBlock.lsp
;;; Makes exact 1:1 copies of a block definition (same geometry, same
;;; attributes) under a new name: the block's name with the next free number,
;;; counting from 1.  e.g. MY_BLOCK1..MY_BLOCK9 exist -> new MY_BLOCK10
;;;
;;; Load: APPLOAD (or drag this file into AutoCAD), then type CLONEBLOCK.
;;; After creating them it asks to place them: just click where each one goes.

(vl-load-com)

;; Copies attribute values (matched by tag) from one placed block to another.
(defun CB:CopyAttValues (from to / vals pair)
  (if (and from to
           (= (vla-get-HasAttributes from) :vlax-true)
           (= (vla-get-HasAttributes to) :vlax-true))
    (progn
      (foreach a (vlax-invoke from 'GetAttributes)
        (setq vals (cons (cons (strcase (vla-get-TagString a)) (vla-get-TextString a)) vals)))
      (foreach a (vlax-invoke to 'GetAttributes)
        (if (setq pair (assoc (strcase (vla-get-TagString a)) vals))
          (vla-put-TextString a (cdr pair))))
    )
  )
)

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

(defun c:CLONEBLOCK (/ doc sel name src parts base digits width qty i newname made newnames place *error* oldattreq srcref last)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object))
        oldattreq (getvar "ATTREQ"))
  (defun *error* (msg)
    (setvar "ATTREQ" oldattreq)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*")) (princ (strcat "\nError: " msg)))
    (princ)
  )

  ;; Pick a block, or press Enter to type the name.
  (setq sel (entsel "\nSelect the block to copy <Enter to type its name>: "))
  (if (and sel (= (cdr (assoc 0 (entget (car sel)))) "INSERT"))
    (setq srcref (vlax-ename->vla-object (car sel))
          name   (vla-get-EffectiveName srcref))
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
            (setq made (strcat made "\n  " newname) qty (1- qty)
                  newnames (cons newname newnames))
          )
        )
        (setq i (1+ i))
      )
      (princ (strcat "\nCreated (exact copies of " name "):" made))

      ;; Place each new block: scale 1, rotation 0, no attribute prompts.
      ;; If you clicked a block at the start, its attribute values are copied
      ;; onto the new one; otherwise the attribute defaults are used.
      (initget "Yes No")
      (setq place (getkword "\nPlace the new block(s) now? [Yes/No] <Yes>: "))
      (if (/= place "No")
        (progn
          (setvar "ATTREQ" 0)
          (foreach n (reverse newnames)
            (princ (strcat "\nPlace " n ":"))
            (setq last (entlast))
            (command "_.-INSERT" n "_S" 1 "_R" 0 pause)
            (while (> (getvar "CMDACTIVE") 0) (command pause))
            (if (and srcref (not (equal last (entlast))))
              (CB:CopyAttValues srcref (vlax-ename->vla-object (entlast))))
          )
          (setvar "ATTREQ" oldattreq)
        )
      )
      (vla-EndUndoMark doc)
    )
  )
  (princ)
)

(princ "\nCloneBlock loaded. Type CLONEBLOCK.")
(princ)
