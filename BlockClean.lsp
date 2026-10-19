;;; BlockClean.lsp
;;; Command: BLOCKCLEAN
;;; Goes through every block definition in the drawing (including nested
;;; and dynamic/anonymous blocks) and sets every object inside it to:
;;;   Layer      = 0
;;;   Linetype   = ByLayer
;;;   Lineweight = ByLayer
;;; Attributes on inserted blocks are updated the same way.
;;; Model/paper space and xrefs are not touched.
;;; Locked layers are temporarily unlocked and re-locked afterwards.

(vl-load-com)

(defun BlockClean:fix (obj)
  (vl-catch-all-apply 'vla-put-Layer (list obj "0"))
  (vl-catch-all-apply 'vla-put-Linetype (list obj "ByLayer"))
  (vl-catch-all-apply 'vla-put-Lineweight (list obj acLnWtByLayer)))

(defun c:BLOCKCLEAN (/ *error* doc locked blkCount objCount)
  (setq doc      (vla-get-ActiveDocument (vlax-get-acad-object))
        locked   '()
        blkCount 0
        objCount 0)

  (defun *error* (msg)
    (foreach l locked (vla-put-Lock l :vlax-true))
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg)))
    (princ))

  (vla-StartUndoMark doc)

  ;; Temporarily unlock all locked layers
  (vlax-for lyr (vla-get-Layers doc)
    (if (= (vla-get-Lock lyr) :vlax-true)
      (progn
        (vla-put-Lock lyr :vlax-false)
        (setq locked (cons lyr locked)))))

  ;; Fix every object inside every block definition
  (vlax-for blk (vla-get-Blocks doc)
    (if (and (= (vla-get-IsLayout blk) :vlax-false)
             (= (vla-get-IsXRef blk) :vlax-false))
      (progn
        (setq blkCount (1+ blkCount))
        (vlax-for obj blk
          (BlockClean:fix obj)
          (setq objCount (1+ objCount))))))

  ;; Fix attributes on inserted blocks (they live outside the definition)
  (vlax-for blk (vla-get-Blocks doc)
    (if (= (vla-get-IsXRef blk) :vlax-false)
      (vlax-for obj blk
        (if (and (= (vla-get-ObjectName obj) "AcDbBlockReference")
                 (= (vla-get-HasAttributes obj) :vlax-true))
          (foreach att (vlax-invoke obj 'GetAttributes)
            (BlockClean:fix att))))))

  ;; Re-lock layers
  (foreach l locked (vla-put-Lock l :vlax-true))
  (setq locked '())

  (vla-Regen doc acAllViewports)
  (vla-EndUndoMark doc)
  (princ (strcat "\n" (itoa objCount) " object(s) in "
                 (itoa blkCount) " block(s) set to layer 0 / ByLayer."))
  (princ))

(princ "\nBlockClean loaded. Type BLOCKCLEAN to run.")
(princ)
